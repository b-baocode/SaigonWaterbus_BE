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

namespace SaigonWaterbus.Application.CustomBookings;

public sealed record ImportCustomBookingPassengersCommand(
    Guid BookingId,
    string FileName,
    byte[] Content) : IRequest<ImportCustomBookingPassengersResult>;

public sealed class ImportCustomBookingPassengersCommandValidator
    : AbstractValidator<ImportCustomBookingPassengersCommand>
{
    private const int MaxManifestFileSizeBytes = 10 * 1024 * 1024;

    public ImportCustomBookingPassengersCommandValidator()
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

public sealed class ImportCustomBookingPassengersCommandHandler
    : IRequestHandler<ImportCustomBookingPassengersCommand, ImportCustomBookingPassengersResult>
{
    private const string PaidBookingPaymentStatus = "Paid";

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IPaymentNotificationSender _paymentNotificationSender;
    private readonly ICustomBookingTicketPdfRenderer _ticketPdfRenderer;
    private readonly TimeProvider _timeProvider;

    public ImportCustomBookingPassengersCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IPaymentNotificationSender paymentNotificationSender,
        ICustomBookingTicketPdfRenderer ticketPdfRenderer,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _paymentNotificationSender = paymentNotificationSender;
        _ticketPdfRenderer = ticketPdfRenderer;
        _timeProvider = timeProvider;
    }

    public async Task<ImportCustomBookingPassengersResult> Handle(
        ImportCustomBookingPassengersCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var booking = await CustomBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.Passengers)
            .Include(x => x.Payments)
            .Include(x => x.Tickets)
                .ThenInclude(x => x.BookingPassenger)
            .Include(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Custom booking not found.");

        if (booking.UserId != userId)
        {
            throw new NotFoundException("Custom booking not found.");
        }

        if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Refunded)
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.BookingStatus),
                "Không thể cập nhật danh sách hành khách cho booking đã hủy hoặc đã hoàn tất.")]);
        }

        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure(nameof(booking.PaymentStatus),
                "Chỉ upload danh sách hành khách sau khi custom booking đã thanh toán đủ.")]);
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var passengers = PassengerManifestParser.Parse(request.FileName, request.Content, today);

        if (passengers.Count > booking.PassengerCount.GetValueOrDefault())
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Content),
                "Danh sách hành khách không được vượt quá số khách đã đăng ký.")]);
        }

        var passengerEntities = passengers
            .Select(x => CustomBookingPassengerSupport.ToEntity(booking.Id, x, today))
            .ToList();
        CustomBookingPassengerSupport.EnsurePassengerTypeCountsMatchRequest(
            booking,
            passengerEntities,
            nameof(request.Content));

        CustomBookingTicketSupport.CancelTicketsBeforeReplacingPassengers(booking);
        _context.Set<BookingPassenger>().RemoveRange(booking.Passengers);
        booking.Passengers = passengerEntities;
        var ticketResult = await CustomBookingTicketSupport.EnsurePassengerTicketsAsync(
            _context,
            booking,
            _timeProvider,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await SendBoardingPassIfNeededAsync(booking, ticketResult, cancellationToken);

        var adultCount = CustomBookingPassengerSupport.CountAdults(booking.Passengers);
        var childCount = CustomBookingPassengerSupport.CountChildren(booking.Passengers);
        var ticketDtos = ticketResult?.Tickets
            .Select(CustomBookingTicketSupport.ToDto)
            .ToList() ?? [];

        return new ImportCustomBookingPassengersResult(
            booking.Id,
            booking.CustomBookingQrToken,
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            adultCount,
            childCount,
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(CustomBookingPassengerSupport.ToDto)
                .ToList(),
            ticketDtos.Count,
            ticketDtos);
    }

    private async Task SendBoardingPassIfNeededAsync(
        Booking booking,
        PassengerTicketEnsureResult? ticketResult,
        CancellationToken cancellationToken)
    {
        var ticket = ticketResult?.CreatedTickets.FirstOrDefault();
        if (ticket is null || string.IsNullOrWhiteSpace(booking.ContactEmail))
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
                Attachments: attachments),
            cancellationToken);
    }

    private IReadOnlyList<EmailAttachment> CreateBoardingPassAttachments(
        Booking booking,
        IReadOnlyList<Ticket> tickets)
    {
        var export = CustomBookingTicketExportSupport.ToDto(booking, tickets);
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
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "MM/dd/yyyy",
        "M/d/yyyy"
    ];

    public static IReadOnlyList<CustomBookingPassengerRequest> Parse(
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
                "File phải có cột tên hành khách và ngày sinh.")]);

        var errors = new List<ValidationFailure>();
        var passengers = new List<CustomBookingPassengerRequest>();

        for (var rowIndex = header.RowIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var fullName = GetCell(row, header.FullNameIndex)?.Trim();
            var dateOfBirthText = GetCell(row, header.DateOfBirthIndex)?.Trim();

            if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(dateOfBirthText))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].FullName",
                    "Tên hành khách là bắt buộc."));
                continue;
            }

            if (!TryParseDateOfBirth(dateOfBirthText, out var dateOfBirth))
            {
                errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].DateOfBirth",
                    "Ngày sinh không hợp lệ. Dùng định dạng yyyy-MM-dd hoặc dd/MM/yyyy."));
                continue;
            }

            if (dateOfBirth > today)
            {
                errors.Add(new ValidationFailure($"Rows[{rowIndex + 1}].DateOfBirth",
                    "Ngày sinh không được ở tương lai."));
                continue;
            }

            passengers.Add(new CustomBookingPassengerRequest(
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
            int? dateOfBirthIndex = null;

            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var header = NormalizeHeader(rows[rowIndex][columnIndex]);
                if (fullNameIndex is null && IsFullNameHeader(header))
                {
                    fullNameIndex = columnIndex;
                }

                if (dateOfBirthIndex is null && IsDateOfBirthHeader(header))
                {
                    dateOfBirthIndex = columnIndex;
                }
            }

            if (fullNameIndex.HasValue && dateOfBirthIndex.HasValue)
            {
                return new HeaderIndexes(rowIndex, fullNameIndex.Value, dateOfBirthIndex.Value);
            }
        }

        return null;
    }

    private static bool TryParseDateOfBirth(string? value, out DateOnly dateOfBirth)
    {
        dateOfBirth = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var serialDate)
            && serialDate is > 0 and < 100000)
        {
            dateOfBirth = DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
            return true;
        }

        if (DateOnly.TryParseExact(
                normalized,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateOfBirth))
        {
            return true;
        }

        return DateOnly.TryParse(normalized, CultureInfo.GetCultureInfo("vi-VN"), out dateOfBirth)
            || DateOnly.TryParse(normalized, CultureInfo.InvariantCulture, out dateOfBirth);
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

    private static bool IsDateOfBirthHeader(string value) =>
        value is "ngaysinh" or "ngaythangnamsinh" or "ngaythangnam" or "dateofbirth"
            or "dob" or "birthdate" or "birthday";

    private sealed record HeaderIndexes(
        int RowIndex,
        int FullNameIndex,
        int DateOfBirthIndex);
}
