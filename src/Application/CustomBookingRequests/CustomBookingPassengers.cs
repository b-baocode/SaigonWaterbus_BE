using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record CustomBookingPassengerInput(
    string? FullName,
    DateOnly DateOfBirth);

public sealed record CustomBookingPassengerDto(
    Guid Id,
    int PassengerOrder,
    string FullName,
    CustomBookingPassengerType PassengerType,
    DateOnly DateOfBirth,
    int AgeOnDepartureDate);

public sealed record CustomBookingPassengerManifestDto(
    Guid CustomBookingRequestId,
    PassengerManifestStatus Status,
    int RequiredPassengerCount,
    int RequiredAdultCount,
    int RequiredChildCount,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    DateTimeOffset? CompletedAt,
    IReadOnlyCollection<CustomBookingPassengerDto> Passengers);

public sealed record CustomBookingPassengerFileRow(
    int RowNumber,
    string? FullName,
    string? PassengerType,
    string? DateOfBirth);

public sealed record CustomBookingPassengerPreviewRowDto(
    int RowNumber,
    string? FullName,
    DateOnly? DateOfBirth,
    int? AgeOnDepartureDate,
    CustomBookingPassengerType? PassengerType);

public sealed record CustomBookingPassengerManifestPreviewDto(
    Guid CustomBookingRequestId,
    bool CanConfirm,
    int RequiredPassengerCount,
    int RequiredAdultCount,
    int RequiredChildCount,
    int PassengerCount,
    int AdultCount,
    int ChildCount,
    IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings,
    IReadOnlyCollection<CustomBookingPassengerPreviewRowDto> Rows);

public sealed record GetCustomBookingPassengerManifestQuery(Guid Id)
    : IRequest<CustomBookingPassengerManifestDto>;

public sealed record PreviewCustomBookingPassengerManifestImportCommand(
    Guid Id,
    IReadOnlyCollection<CustomBookingPassengerFileRow> Rows)
    : IRequest<CustomBookingPassengerManifestPreviewDto>;

public sealed record UpdateCustomBookingPassengerManifestCommand(
    Guid Id,
    IReadOnlyCollection<CustomBookingPassengerInput> Passengers)
    : IRequest<CustomBookingPassengerManifestDto>;

public sealed class UpdateCustomBookingPassengerManifestCommandValidator
    : AbstractValidator<UpdateCustomBookingPassengerManifestCommand>
{
    public UpdateCustomBookingPassengerManifestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.Passengers).NotNull().WithMessage("Danh sách hành khách là bắt buộc.");
        RuleFor(x => x.Passengers)
            .Must(x => x is null || x.Count <= 500)
            .WithMessage("Danh sách hành khách tối đa 500 người.");

        RuleForEach(x => x.Passengers).ChildRules(passenger =>
        {
            passenger.RuleFor(x => x.FullName)
                .NotEmpty()
                .WithMessage("Họ tên hành khách là bắt buộc.")
                .MaximumLength(150)
                .WithMessage("Họ tên hành khách tối đa 150 ký tự.");
            passenger.RuleFor(x => x.DateOfBirth)
                .Must(x => x != default)
                .WithMessage("Ngày sinh hành khách là bắt buộc.");
        });
    }
}

public sealed class GetCustomBookingPassengerManifestQueryHandler
    : IRequestHandler<GetCustomBookingPassengerManifestQuery, CustomBookingPassengerManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetCustomBookingPassengerManifestQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingPassengerManifestDto> Handle(
        GetCustomBookingPassengerManifestQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .Include(x => x.Passengers)
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanView(customRequest, actor);
        return CustomBookingPassengerManifestSupport.CreateDto(customRequest);
    }
}

public sealed class PreviewCustomBookingPassengerManifestImportCommandHandler
    : IRequestHandler<PreviewCustomBookingPassengerManifestImportCommand, CustomBookingPassengerManifestPreviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public PreviewCustomBookingPassengerManifestImportCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CustomBookingPassengerManifestPreviewDto> Handle(
        PreviewCustomBookingPassengerManifestImportCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingPassengerManifestSupport.EnsureCanUpdate(customRequest, actor);
        return CustomBookingPassengerManifestSupport.CreatePreviewDto(customRequest, request.Rows);
    }
}

public sealed class UpdateCustomBookingPassengerManifestCommandHandler
    : IRequestHandler<UpdateCustomBookingPassengerManifestCommand, CustomBookingPassengerManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingConfirmationEmailSender _confirmationEmailSender;

    public UpdateCustomBookingPassengerManifestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingConfirmationEmailSender confirmationEmailSender)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _confirmationEmailSender = confirmationEmailSender;
    }

    public async Task<CustomBookingPassengerManifestDto> Handle(
        UpdateCustomBookingPassengerManifestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .Include(x => x.Passengers)
            .Include(x => x.Tickets)
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingPassengerManifestSupport.EnsureCanUpdate(customRequest, actor);
        CustomBookingPassengerManifestSupport.ValidatePassengers(customRequest, request.Passengers);
        var isFullyPaid = CustomBookingPaymentSupport.IsFullyPaid(customRequest.Quote);
        var shouldSendConfirmationEmail = isFullyPaid
            && (
            customRequest.PassengerManifestStatus != PassengerManifestStatus.Completed
            || customRequest.Tickets.All(x => x.Status != CustomBookingTicketStatus.Active));

        _context.Set<CustomBookingPassenger>().RemoveRange(customRequest.Passengers);
        customRequest.Passengers.Clear();

        var passengerOrder = 1;
        foreach (var passenger in request.Passengers)
        {
            var entity = new CustomBookingPassenger
            {
                CustomBookingRequestId = customRequest.Id,
                CustomBookingRequest = customRequest,
                PassengerOrder = passengerOrder++,
                FullName = CustomBookingPassengerManifestSupport.NormalizeRequiredText(passenger.FullName),
                PassengerType = CustomBookingPassengerManifestSupport.ResolvePassengerType(
                    passenger.DateOfBirth,
                    customRequest.DepartureDate),
                DateOfBirth = passenger.DateOfBirth
            };

            _context.Set<CustomBookingPassenger>().Add(entity);
        }

        customRequest.PassengerManifestStatus = PassengerManifestStatus.Completed;
        var now = _timeProvider.GetUtcNow();
        customRequest.PassengerManifestCompletedAt = now;
        customRequest.PassengerManifestCompletedByUserId = actor.Id;
        if (shouldSendConfirmationEmail)
        {
            await CustomBookingTicketSupport.EnsureActiveTicketAsync(
                _context,
                customRequest,
                now,
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (shouldSendConfirmationEmail)
        {
            await _confirmationEmailSender.SendConfirmationAsync(customRequest, cancellationToken);
        }

        return CustomBookingPassengerManifestSupport.CreateDto(customRequest);
    }
}

internal static class CustomBookingPassengerManifestSupport
{
    private const int ChildAgeExclusive = 11;

    public static void EnsureCanUpdate(CustomBookingRequest customRequest, User actor)
    {
        var canUpdate = AuthSupport.IsAdmin(actor)
            || (AuthSupport.IsManager(actor) && customRequest.AssignedManagerUserId == actor.Id)
            || (AuthSupport.IsCustomer(actor) && customRequest.UserId == actor.Id);

        if (!canUpdate)
        {
            throw new ForbiddenAccessException();
        }

        if (customRequest.Status != CustomBookingRequestStatus.Confirmed)
        {
            throw AuthSupport.CreateValidationException(
                nameof(customRequest.Status),
                "Chỉ cập nhật danh sách hành khách sau khi booking đã xác nhận.");
        }

        if (customRequest.Tickets.Any(x => x.QrUsedAt.HasValue || x.Status == CustomBookingTicketStatus.Used)
            || customRequest.PassengerManifestStatus == PassengerManifestStatus.Locked)
        {
            throw AuthSupport.CreateValidationException(
                nameof(customRequest.PassengerManifestStatus),
                "Danh sách hành khách đã khóa sau khi check-in.");
        }
    }

    public static void ValidatePassengers(
        CustomBookingRequest customRequest,
        IReadOnlyCollection<CustomBookingPassengerInput> passengers)
    {
        var failures = new List<ValidationFailure>();
        if (passengers.Count != customRequest.PassengerCount)
        {
            failures.Add(new ValidationFailure(
                nameof(UpdateCustomBookingPassengerManifestCommand.Passengers),
                $"Danh sách phải có đúng {customRequest.PassengerCount} hành khách."));
        }

        var adultCount = passengers.Count(x =>
            x.DateOfBirth != default
            && x.DateOfBirth <= customRequest.DepartureDate
            && ResolvePassengerType(x.DateOfBirth, customRequest.DepartureDate) == CustomBookingPassengerType.Adult);
        var childCount = passengers.Count(x =>
            x.DateOfBirth != default
            && x.DateOfBirth <= customRequest.DepartureDate
            && ResolvePassengerType(x.DateOfBirth, customRequest.DepartureDate) == CustomBookingPassengerType.Child);

        if (adultCount != customRequest.AdultCount)
        {
            failures.Add(new ValidationFailure(
                nameof(UpdateCustomBookingPassengerManifestCommand.Passengers),
                $"Danh sách phải có đúng {customRequest.AdultCount} người lớn."));
        }

        if (childCount != customRequest.ChildCount)
        {
            failures.Add(new ValidationFailure(
                nameof(UpdateCustomBookingPassengerManifestCommand.Passengers),
                $"Danh sách phải có đúng {customRequest.ChildCount} trẻ em."));
        }

        var rowNumber = 1;
        foreach (var passenger in passengers)
        {
            if (passenger.DateOfBirth > customRequest.DepartureDate)
            {
                failures.Add(new ValidationFailure(
                    $"{nameof(UpdateCustomBookingPassengerManifestCommand.Passengers)}[{rowNumber}].{nameof(passenger.DateOfBirth)}",
                    $"Dòng {rowNumber}: ngày sinh không được lớn hơn ngày khởi hành."));
            }

            rowNumber++;
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    public static CustomBookingPassengerManifestDto CreateDto(CustomBookingRequest customRequest)
    {
        var passengers = customRequest.Passengers
            .OrderBy(x => x.PassengerOrder)
            .Select(x => new CustomBookingPassengerDto(
                x.Id,
                x.PassengerOrder,
                x.FullName,
                x.PassengerType,
                x.DateOfBirth,
                CalculateAge(x.DateOfBirth, customRequest.DepartureDate)))
            .ToArray();

        return new CustomBookingPassengerManifestDto(
            customRequest.Id,
            customRequest.PassengerManifestStatus,
            customRequest.PassengerCount,
            customRequest.AdultCount,
            customRequest.ChildCount,
            passengers.Length,
            passengers.Count(x => x.PassengerType == CustomBookingPassengerType.Adult),
            passengers.Count(x => x.PassengerType == CustomBookingPassengerType.Child),
            customRequest.PassengerManifestCompletedAt,
            passengers);
    }

    public static CustomBookingPassengerManifestPreviewDto CreatePreviewDto(
        CustomBookingRequest customRequest,
        IReadOnlyCollection<CustomBookingPassengerFileRow> rows)
    {
        var previewRows = new List<CustomBookingPassengerPreviewRowDto>();
        var rowErrors = new List<IReadOnlyCollection<string>>();
        var rowWarnings = new List<IReadOnlyCollection<string>>();
        var globalErrors = new List<string>();
        var globalWarnings = new List<string>();

        foreach (var row in rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var fullName = NormalizeOptionalText(row.FullName);

            if (string.IsNullOrWhiteSpace(fullName))
            {
                errors.Add("Thiếu họ tên.");
            }

            DateOnly? dateOfBirth = null;
            int? age = null;
            CustomBookingPassengerType? passengerType = null;
            if (string.IsNullOrWhiteSpace(row.DateOfBirth))
            {
                errors.Add("Thiếu ngày sinh.");
            }
            else if (!TryParsePassengerDate(row.DateOfBirth, out var parsedDate))
            {
                errors.Add("Ngày sinh phải dùng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd.");
            }
            else if (parsedDate > customRequest.DepartureDate)
            {
                errors.Add("Ngày sinh không được lớn hơn ngày khởi hành.");
            }
            else
            {
                dateOfBirth = parsedDate;
                age = CalculateAge(parsedDate, customRequest.DepartureDate);
                passengerType = ResolvePassengerType(parsedDate, customRequest.DepartureDate);
                if (!string.IsNullOrWhiteSpace(row.PassengerType)
                    && TryParsePassengerType(row.PassengerType, out var providedType)
                    && providedType != passengerType)
                {
                    warnings.Add($"PassengerType trong file là {providedType}, hệ thống tự tính là {passengerType}.");
                }
            }

            previewRows.Add(new CustomBookingPassengerPreviewRowDto(
                row.RowNumber,
                fullName,
                dateOfBirth,
                age,
                passengerType));
            rowErrors.Add(errors);
            rowWarnings.Add(warnings);
        }

        var validRows = previewRows
            .Where((_, index) => rowErrors[index].Count == 0)
            .ToArray();
        var adultCount = validRows.Count(x => x.PassengerType == CustomBookingPassengerType.Adult);
        var childCount = validRows.Count(x => x.PassengerType == CustomBookingPassengerType.Child);

        if (validRows.Length != customRequest.PassengerCount)
        {
            globalErrors.Add($"Danh sách hợp lệ phải có đúng {customRequest.PassengerCount} hành khách.");
        }

        if (adultCount != customRequest.AdultCount)
        {
            globalErrors.Add($"Danh sách hợp lệ phải có đúng {customRequest.AdultCount} người lớn.");
        }

        if (childCount != customRequest.ChildCount)
        {
            globalErrors.Add($"Danh sách hợp lệ phải có đúng {customRequest.ChildCount} trẻ em.");
        }

        if (rowErrors.Any(x => x.Count > 0))
        {
            globalErrors.Add("File còn dòng lỗi, vui lòng sửa trước khi xác nhận.");
        }

        if (rowWarnings.Any(x => x.Count > 0))
        {
            globalWarnings.Add("Một số PassengerType trong file khác với kết quả hệ thống tự tính theo ngày sinh.");
        }

        return new CustomBookingPassengerManifestPreviewDto(
            customRequest.Id,
            globalErrors.Count == 0,
            customRequest.PassengerCount,
            customRequest.AdultCount,
            customRequest.ChildCount,
            validRows.Length,
            adultCount,
            childCount,
            globalErrors,
            globalWarnings,
            previewRows);
    }

    public static int CalculateAge(DateOnly dateOfBirth, DateOnly atDate)
    {
        var age = atDate.Year - dateOfBirth.Year;
        if (atDate < dateOfBirth.AddYears(age))
        {
            age--;
        }

        return age;
    }

    public static CustomBookingPassengerType ResolvePassengerType(DateOnly dateOfBirth, DateOnly departureDate) =>
        CalculateAge(dateOfBirth, departureDate) < ChildAgeExclusive
            ? CustomBookingPassengerType.Child
            : CustomBookingPassengerType.Adult;

    public static bool TryParsePassengerDate(string value, out DateOnly date)
    {
        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(
                trimmed,
                CustomBookingPassengerManifestFileParser.DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return true;
        }

        if (DateTime.TryParseExact(
                trimmed,
                CustomBookingPassengerManifestFileParser.DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var exactDateTime))
        {
            date = DateOnly.FromDateTime(exactDateTime);
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var serialDate)
            && serialDate > 0)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
            return true;
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.GetCultureInfo("vi-VN"),
                DateTimeStyles.AllowWhiteSpaces,
                out var vietnameseDateTime)
            || DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out vietnameseDateTime))
        {
            date = DateOnly.FromDateTime(vietnameseDateTime);
            return true;
        }

        return false;
    }

    public static bool TryParsePassengerType(string value, out CustomBookingPassengerType passengerType)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "adult":
            case "nguoilon":
                passengerType = CustomBookingPassengerType.Adult;
                return true;
            case "child":
            case "treem":
                passengerType = CustomBookingPassengerType.Child;
                return true;
            default:
                passengerType = default;
                return false;
        }
    }

    public static string NormalizeRequiredText(string? value) => value!.Trim();

    public static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class CustomBookingPassengerManifestFileParser
{
    public static readonly string[] DateFormats =
    [
        "dd/MM/yyyy",
        "dd-MM-yyyy",
        "yyyy-MM-dd",
        "d/M/yyyy",
        "d-M-yyyy"
    ];

    public static readonly string[] DateTimeFormats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFF",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFK",
        "yyyy-MM-ddTHH:mm:ss.fffK"
    ];

    public static IReadOnlyCollection<CustomBookingPassengerFileRow> Parse(
        string fileName,
        Stream stream)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => ParseCsv(stream),
            ".xlsx" => ParseXlsx(stream),
            _ => throw AuthSupport.CreateValidationException(
                "file",
                "File danh sách hành khách chỉ hỗ trợ .csv hoặc .xlsx.")
        };
    }

    private static IReadOnlyCollection<CustomBookingPassengerFileRow> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        var lines = content
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (lines.Length < 2)
        {
            throw AuthSupport.CreateValidationException("file", "File phải có header và ít nhất một hành khách.");
        }

        var delimiter = lines.Take(10).Sum(x => x.Count(c => c == ';'))
            > lines.Take(10).Sum(x => x.Count(c => c == ','))
                ? ';'
                : ',';
        var rows = lines
            .Select((line, index) => new SpreadsheetRow(index + 1, ParseDelimitedLine(line, delimiter)))
            .Where(row => row.Values.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToArray();

        return ParseRows(rows);
    }

    private static IReadOnlyCollection<CustomBookingPassengerFileRow> ParseXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetEntry = GetFirstWorksheetEntry(archive)
            ?? throw AuthSupport.CreateValidationException("file", "File Excel phải có sheet đầu tiên.");

        using var worksheetStream = worksheetEntry.Open();
        var document = XDocument.Load(worksheetStream);
        var ns = document.Root!.Name.Namespace;
        var rows = document.Descendants(ns + "row")
            .Select(row => new SpreadsheetRow(
                ReadXlsxRowNumber(row),
                ReadXlsxRow(row, ns, sharedStrings)))
            .Where(row => row.Values.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToArray();

        if (rows.Length < 2)
        {
            throw AuthSupport.CreateValidationException("file", "File phải có header và ít nhất một hành khách.");
        }

        return ParseRows(rows);
    }

    private sealed record SpreadsheetRow(int RowNumber, IReadOnlyList<string> Values);

    private static ZipArchiveEntry? GetFirstWorksheetEntry(ZipArchive archive)
    {
        var relationshipId = ReadFirstSheetRelationshipId(archive);
        if (!string.IsNullOrWhiteSpace(relationshipId))
        {
            var target = ReadWorkbookRelationshipTarget(archive, relationshipId);
            if (!string.IsNullOrWhiteSpace(target))
            {
                var entry = archive.GetEntry(ResolveWorkbookRelationshipTarget(target));
                if (entry is not null)
                {
                    return entry;
                }
            }
        }

        return archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries
                .Where(x => x.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                    && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
    }

    private static string? ReadFirstSheetRelationshipId(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
        {
            return null;
        }

        using var stream = workbookEntry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var relationshipNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        return document.Descendants(ns + "sheet")
            .Select(x => x.Attribute(relationshipNs + "id")?.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string? ReadWorkbookRelationshipTarget(ZipArchive archive, string relationshipId)
    {
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relationshipsEntry is null)
        {
            return null;
        }

        using var stream = relationshipsEntry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        return document.Descendants(ns + "Relationship")
            .Where(x => x.Attribute("Id")?.Value == relationshipId)
            .Select(x => x.Attribute("Target")?.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string ResolveWorkbookRelationshipTarget(string target)
    {
        var path = target.Replace('\\', '/');
        path = path.StartsWith("/", StringComparison.Ordinal)
            ? path[1..]
            : $"xl/{path}";

        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    break;
                case ".." when segments.Count > 0:
                    segments.RemoveAt(segments.Count - 1);
                    break;
                case "..":
                    break;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        return string.Join("/", segments);
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
        var ns = document.Root!.Name.Namespace;
        return document.Descendants(ns + "si")
            .Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value)))
            .ToArray();
    }

    private static string[] ReadXlsxRow(
        XElement row,
        XNamespace ns,
        IReadOnlyList<string> sharedStrings)
    {
        var cells = new SortedDictionary<int, string>();
        foreach (var cell in row.Elements(ns + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var columnIndex = GetColumnIndex(reference);
            cells[columnIndex] = ReadXlsxCell(cell, ns, sharedStrings);
        }

        if (cells.Count == 0)
        {
            return [];
        }

        var values = new string[cells.Keys.Max() + 1];
        foreach (var cell in cells)
        {
            values[cell.Key] = cell.Value;
        }

        return values;
    }

    private static string ReadXlsxCell(
        XElement cell,
        XNamespace ns,
        IReadOnlyList<string> sharedStrings)
    {
        var cellType = cell.Attribute("t")?.Value;
        if (cellType == "inlineStr")
        {
            return string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
        }

        var value = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (cellType == "s"
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static int GetColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return 0;
        }

        var index = 0;
        foreach (var c in reference)
        {
            if (!char.IsAsciiLetter(c))
            {
                break;
            }

            index = index * 26 + char.ToUpperInvariant(c) - 'A' + 1;
        }

        return Math.Max(0, index - 1);
    }

    private static int ReadXlsxRowNumber(XElement row)
    {
        var rowNumberText = row.Attribute("r")?.Value;
        return int.TryParse(rowNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber)
            ? rowNumber
            : 1;
    }

    private static IReadOnlyCollection<CustomBookingPassengerFileRow> ParseRows(
        IReadOnlyList<SpreadsheetRow> rows)
    {
        var headerIndex = -1;
        Dictionary<string, int>? headerMap = null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (TryBuildHeaderMap(rows[i].Values, out var candidateHeaderMap))
            {
                headerIndex = i;
                headerMap = candidateHeaderMap;
                break;
            }
        }

        if (headerMap is null)
        {
            throw AuthSupport.CreateValidationException(
                "file",
                "File thiếu header bắt buộc: FullName/Tên và DateOfBirth/Ngày sinh.");
        }

        var parsedRows = new List<CustomBookingPassengerFileRow>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Values.All(x => string.IsNullOrWhiteSpace(x)))
            {
                continue;
            }

            parsedRows.Add(new CustomBookingPassengerFileRow(
                row.RowNumber,
                GetOptionalCell(row.Values, headerMap, "fullName"),
                GetOptionalCell(row.Values, headerMap, "passengerType"),
                GetOptionalCell(row.Values, headerMap, "dateOfBirth")));
        }

        if (parsedRows.Count == 0)
        {
            throw AuthSupport.CreateValidationException("file", "File phải có header và ít nhất một hành khách.");
        }

        return parsedRows;
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> header)
    {
        if (TryBuildHeaderMap(header, out var result))
        {
            return result;
        }

        throw AuthSupport.CreateValidationException(
            "file",
            "File thiếu header bắt buộc: FullName/Tên và DateOfBirth/Ngày sinh.");
    }

    private static bool TryBuildHeaderMap(
        IReadOnlyList<string> header,
        out Dictionary<string, int> result)
    {
        result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var key = ResolveHeaderKey(header[i]);
            if (key is not null && !result.ContainsKey(key))
            {
                result[key] = i;
            }
        }

        foreach (var required in new[] { "fullName", "dateOfBirth" })
        {
            if (!result.ContainsKey(required))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ResolveHeaderKey(string header)
    {
        var normalized = NormalizeHeader(header);

        return normalized switch
        {
            "fullname" or "name" or "passengername" or "ten" or "tenhanhkhach" or "tenkhach"
                or "hoten" or "hovaten" or "hotenhanhkhach" or "hovatenhanhkhach" => "fullName",
            "passengertype" or "type" or "loaikhach" => "passengerType",
            "dateofbirth" or "dob" or "birthday" or "birthdate" or "ngaysinh" or "ngaythangnamsinh"
                or "ngaysinhnhat" or "namsinh" => "dateOfBirth",
            _ => null
        };
    }

    private static string NormalizeHeader(string header)
    {
        var decomposed = header.Trim().Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                normalized.Append(char.ToLowerInvariant(c));
            }
        }

        return normalized.ToString().Replace('đ', 'd');
    }

    private static string? GetOptionalCell(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headerMap,
        string key)
    {
        return headerMap.TryGetValue(key, out var index) && index < row.Count
            ? CustomBookingPassengerManifestSupport.NormalizeOptionalText(row[index])
            : null;
    }

    private static string[] ParseDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        values.Add(current.ToString());
        return values.ToArray();
    }
}
