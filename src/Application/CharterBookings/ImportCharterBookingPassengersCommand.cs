using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record ImportCharterBookingPassengersCommand(
    Guid BookingId,
    string FileName,
    byte[] Content) : IRequest<ImportCharterBookingPassengersResult>;

public sealed class ImportCharterBookingPassengersCommandValidator
    : AbstractValidator<ImportCharterBookingPassengersCommand>
{
    private const int MaxManifestFileSizeBytes = 10 * 1024 * 1024;

    public ImportCharterBookingPassengersCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.Content.Length)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxManifestFileSizeBytes)
            .WithMessage("File danh sách hành khách tối đa 10 MB.");
    }
}

public sealed class ImportCharterBookingPassengersCommandHandler
    : IRequestHandler<ImportCharterBookingPassengersCommand, ImportCharterBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly ICharterBookingTicketPdfRenderer _ticketPdfRenderer;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public ImportCharterBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        ICharterBookingTicketPdfRenderer ticketPdfRenderer,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _ticketPdfRenderer = ticketPdfRenderer;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<ImportCharterBookingPassengersResult> Handle(
        ImportCharterBookingPassengersCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Passengers)
            .Include(x => x.Payments)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Boat)
            .Include(x => x.CharterBoats)
                .ThenInclude(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Charter booking not found.");
        }

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể cập nhật danh sách hành khách cho booking đã hủy hoặc đã hoàn tất.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ upload danh sách hành khách sau khi charter booking đã thanh toán đủ.")]);
        }

        var now = _timeProvider.GetUtcNow();
        CharterBookingPassengerSupport.EnsureManifestCanBeUpdatedBeforeCutoff(
            booking,
            now,
            nameof(request.Content));

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var passengers = PassengerManifestParser.Parse(request.FileName, request.Content, today);
        CharterBookingPassengerSupport.EnsurePassengerCountDoesNotExceedSelectedBoatCapacity(
            booking,
            passengers.Count,
            nameof(request.Content));

        var passengerEntities = passengers
            .Select(x => CharterBookingPassengerSupport.ToEntity(booking.Id, x, today))
            .ToList();

        CharterBookingTicketSupport.CancelTicketsBeforeReplacingPassengers(booking);
        _context.Set<BookingPassenger>().RemoveRange(booking.Passengers);
        booking.Passengers = passengerEntities;
        booking.PassengerCount = passengerEntities.Count;
        booking.AdultCount = CharterBookingPassengerSupport.CountAdults(passengerEntities);
        booking.ChildCount = CharterBookingPassengerSupport.CountChildren(passengerEntities);
        var ticketResult = await CharterBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);
        var additionalInsuranceAmount = CharterBookingInsuranceSupport.ApplyPassengerQuantityIncrease(
            booking,
            passengerEntities.Count,
            now);

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "PassengersImported",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);
        await SendBoardingPassIfNeededAsync(booking, ticketResult, cancellationToken);

        return CharterBookingPassengerResultSupport.ToImportResult(
            booking,
            ticketResult?.Tickets,
            additionalInsuranceAmount);
    }

    private async Task SendBoardingPassIfNeededAsync(
        Booking booking,
        PassengerTicketEnsureResult? ticketResult,
        CancellationToken cancellationToken)
    {
        var ticket = ticketResult?.CreatedTickets.FirstOrDefault();
        if (ticket is null
            || string.IsNullOrWhiteSpace(booking.ContactEmail)
            || !string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount > 0)
        {
            return;
        }

        var paidPayment = booking.Payments
            .Where(x => PaymentSupport.IsPaid(x.PaymentStatus))
            .OrderByDescending(x => x.PaidAt ?? x.Created)
            .FirstOrDefault();
        if (paidPayment?.PaidAt is null)
        {
            return;
        }

        var bookingNotification = PaymentSupport.CreatePaymentSucceededNotification(booking, paidPayment);
        var attachments = CreateBoardingPassAttachments(booking, ticketResult!.Tickets);
        await _paymentNotificationSender.SendBoardingPassAsync(
            new BoardingPassNotification(
                bookingNotification,
                ticket.TicketCode,
                ticket.QrToken,
                Attachments: attachments,
                PassengerName: ticket.BookingPassenger?.FullName),
            cancellationToken);
    }

    private IReadOnlyList<EmailAttachment> CreateBoardingPassAttachments(
        Booking booking,
        IReadOnlyList<Ticket> tickets)
    {
        var export = CharterBookingTicketExportSupport.ToDto(booking, tickets);
        var pdfBytes = _ticketPdfRenderer.Render(export);

        return
        [
            new EmailAttachment(
                $"{SanitizeFileName(booking.BookingCode)}-boarding-pass.pdf",
                "application/pdf",
                pdfBytes)
        ];
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeValue = new string(value.Select(x => invalidChars.Contains(x) ? '-' : x).ToArray());
        return string.IsNullOrWhiteSpace(safeValue) ? "boarding-pass" : safeValue;
    }
}

internal static class PassengerManifestParser
{
    public static IReadOnlyList<CharterBookingPassengerRequest> Parse(
        string fileName,
        byte[] content,
        DateOnly today)
    {
        var rows = ReadRows(fileName, content);
        if (rows.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(content),
                "File danh sách hành khách không có dữ liệu.")]);
        }

        var header = FindHeader(rows)
            ?? throw new ValidationException([new ValidationFailure(nameof(content),
                "File phải có cột tên hành khách và năm sinh/ngày sinh.")]);

        var errors = new List<ValidationFailure>();
        var passengers = new List<CharterBookingPassengerRequest>();

        for (var rowIndex = header.RowIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var fullName = GetCell(row, header.FullNameIndex)?.Trim();
            var birthInfoText = GetCell(row, header.BirthInfoIndex)?.Trim();

            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(birthInfoText))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].FullName",
                    "Tên hành khách là bắt buộc."));
                continue;
            }

            if (CharterBookingPassengerSupport.TryParseBirthYear(birthInfoText, out var birthYear))
            {
                if (!CharterBookingPassengerSupport.IsValidBirthYear(birthYear, today))
                {
                    errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].BirthYear",
                        birthYear > today.Year ? "Năm sinh không được ở tương lai." : "Năm sinh không hợp lệ."));
                    continue;
                }

                passengers.Add(new CharterBookingPassengerRequest(fullName, null, birthYear));
                continue;
            }

            if (!TryParseDateOfBirth(birthInfoText, out var dateOfBirth))
            {
                errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].BirthInfo",
                    "Năm sinh/ngày sinh không hợp lệ. Dùng năm yyyy hoặc ngày yyyy-MM-dd/dd/MM/yyyy."));
                continue;
            }

            if (dateOfBirth > today)
            {
                errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].DateOfBirth",
                    "Ngày sinh không được ở tương lai."));
                continue;
            }

            passengers.Add(new CharterBookingPassengerRequest(
                fullName,
                dateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (passengers.Count == 0)
        {
            throw new ValidationException([new ValidationFailure(nameof(content),
                "File không có hành khách hợp lệ.")]);
        }

        return passengers;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadRows(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ReadXlsxRows(content),
            ".csv" => ReadDelimitedRows(content, ','),
            ".tsv" => ReadDelimitedRows(content, '\t'),
            ".txt" => ReadDelimitedRows(content, null),
            _ => throw new ValidationException([new ValidationFailure(nameof(fileName),
                "File danh sách hành khách chỉ hỗ trợ .xlsx, .csv, .tsv hoặc .txt.")])
        };
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadDelimitedRows(byte[] content, char? preferredDelimiter)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('\uFEFF');
        var delimiter = preferredDelimiter ?? DetectDelimiter(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (current == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (current == delimiter && !inQuotes)
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if ((current == '\n' || current == '\r') && !inQuotes)
            {
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                AddRowIfNotEmpty(rows, row);
                row = [];
                cell.Clear();
                continue;
            }

            cell.Append(current);
        }

        row.Add(cell.ToString());
        AddRowIfNotEmpty(rows, row);

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadXlsxRows(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var sharedStrings = ReadSharedStrings(archive);
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
                ?? archive.Entries
                    .Where(x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                        && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.FullName)
                    .FirstOrDefault()
                ?? throw new ValidationException([new ValidationFailure(nameof(content),
                    "File Excel không có worksheet.")]);

            using var sheetStream = sheetEntry.Open();
            var document = XDocument.Load(sheetStream);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var rows = new List<IReadOnlyList<string>>();

            foreach (var rowElement in document.Descendants(ns + "row"))
            {
                var values = new Dictionary<int, string>();
                foreach (var cellElement in rowElement.Elements(ns + "c"))
                {
                    var columnIndex = GetColumnIndex(cellElement.Attribute("r")?.Value);
                    if (columnIndex < 0)
                    {
                        columnIndex = values.Count;
                    }

                    values[columnIndex] = ReadCellValue(cellElement, sharedStrings);
                }

                var maxColumn = values.Keys.DefaultIfEmpty(-1).Max();
                if (maxColumn < 0)
                {
                    continue;
                }

                var row = Enumerable.Range(0, maxColumn + 1)
                    .Select(index => values.TryGetValue(index, out var value) ? value : string.Empty)
                    .ToList();
                AddRowIfNotEmpty(rows, row);
            }

            return rows;
        }
        catch (InvalidDataException ex)
        {
            throw new ValidationException([new ValidationFailure(nameof(content),
                $"File Excel không hợp lệ: {ex.Message}")]);
        }
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        return document.Descendants(ns + "si")
            .Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cellElement, IReadOnlyList<string> sharedStrings)
    {
        var ns = cellElement.Name.Namespace;
        var type = cellElement.Attribute("t")?.Value;
        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cellElement.Descendants(ns + "t").Select(x => x.Value));
        }

        var value = cellElement.Element(ns + "v")?.Value;
        if (value is null)
        {
            return string.Concat(cellElement.Descendants(ns + "t").Select(x => x.Value));
        }

        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex)
            && sharedIndex >= 0
            && sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return value;
    }

    private static HeaderIndexes? FindHeader(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var limit = Math.Min(rows.Count, 20);
        for (var rowIndex = 0; rowIndex < limit; rowIndex++)
        {
            int? fullNameIndex = null;
            int? birthInfoIndex = null;

            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var header = NormalizeHeader(rows[rowIndex][columnIndex]);
                if (fullNameIndex is null && IsFullNameHeader(header))
                {
                    fullNameIndex = columnIndex;
                }

                if (birthInfoIndex is null && IsBirthInfoHeader(header))
                {
                    birthInfoIndex = columnIndex;
                }
            }

            if (fullNameIndex.HasValue && birthInfoIndex.HasValue)
            {
                return new HeaderIndexes(rowIndex, fullNameIndex.Value, birthInfoIndex.Value);
            }
        }

        return null;
    }

    private static bool TryParseDateOfBirth(string? value, out DateOnly dateOfBirth)
    {
        if (CharterBookingPassengerSupport.TryParseDateOfBirth(value, out dateOfBirth))
        {
            return true;
        }

        dateOfBirth = default;
        return false;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n').FirstOrDefault() ?? string.Empty;
        var tabCount = firstLine.Count(x => x == '\t');
        var commaCount = firstLine.Count(x => x == ',');
        var semicolonCount = firstLine.Count(x => x == ';');

        if (tabCount >= commaCount && tabCount >= semicolonCount && tabCount > 0)
        {
            return '\t';
        }

        return semicolonCount > commaCount ? ';' : ',';
    }

    private static string? GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return -1;
        }

        var value = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            value = value * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return value == 0 ? -1 : value - 1;
    }

    private static void AddRowIfNotEmpty(List<IReadOnlyList<string>> rows, IReadOnlyList<string> row)
    {
        if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
        {
            rows.Add(row);
        }
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static bool IsFullNameHeader(string value) =>
        value is "hoten" or "hovaten" or "ten" or "fullname" or "name" or "passengername"
            or "tenhanhkhach" or "hanhkhach" or "khachhang";

    private static bool IsBirthInfoHeader(string value) =>
        value is "ngaysinh" or "ngaythangnamsinh" or "ngaythangnam" or "dateofbirth"
            or "dob" or "birthdate" or "birthday" or "namsinh" or "birthyear" or "year";

    private sealed record HeaderIndexes(
        int RowIndex,
        int FullNameIndex,
        int BirthInfoIndex);
}
