using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using System.Linq;
using System.Text.RegularExpressions;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;
using static SaigonWaterbus.Application.CharterBookings.CharterBookingPassengerSupport;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterBookingCommand(
    DateOnly DepartureDate,
    BoatRentalUnit? RentalUnit,
    int? DurationValue,
    int? AdultCount = null,
    int? ChildCount = null,
    IReadOnlyList<CharterBookingPassengerRequest>? Passengers = null,
    TimeOnly? StartTime = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null,
    IReadOnlyList<CreateCharterBookingItineraryStopRequest>? ItineraryStops = null,
    IReadOnlyList<CreateCharterBookingBoatRequest>? RequestedBoats = null,
    string? SpecialRequests = null,
    string? ContactName = null,
    string? ContactPhone = null,
    string? ContactEmail = null,
    bool? InsuranceSelected = null,
    Guid? InsurancePackageId = null) : IRequest<CreateCharterBookingResult>;

public sealed class CreateCharterBookingCommandValidator : AbstractValidator<CreateCharterBookingCommand>
{
    private const int AdultAgeThreshold = 12;

    public CreateCharterBookingCommandValidator()
    {
        RuleFor(x => x.DepartureDate).NotEqual(default(DateOnly)).WithMessage("Ngày khởi hành là bắt buộc.");
        RuleFor(x => x.RentalUnit!.Value)
            .IsInEnum()
            .When(x => x.RentalUnit.HasValue)
            .WithMessage("Đơn vị thuê tàu chỉ được là Hour hoặc Day.");
        RuleFor(x => x.DurationValue!.Value).GreaterThan(0).LessThanOrEqualTo(60)
            .When(x => x.DurationValue.HasValue)
            .WithMessage("Thời lượng thuê phải từ 1 đến 60.");
        RuleFor(x => x.StartTime!.Value)
            .Must(BeWithinCharterStartTimeWindow)
            .When(x => x.StartTime.HasValue)
            .WithMessage("Giờ bắt đầu charter phải nằm trong khung 07:00 đến trước 22:00.");
        RuleFor(x => x.DepartureDate)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Ngày khởi hành không được ở quá khứ.");

        // Validate: phải có Passengers HOẶC (AdultCount + ChildCount)
        RuleFor(x => x)
            .Must(x => HasPassengers(x))
            .WithMessage("Phải cung cấp danh sách hành khách (passengers) hoặc số người lớn/trẻ em.");

        // Validate hành khách
        RuleFor(x => x)
            .Must(x => !HasPassengers(x) || ValidatePassengerCount(x) == null)
            .WithMessage(x => ValidatePassengerCount(x) ?? string.Empty)
            .When(x => HasPassengers(x));

        RuleForEach(x => x.Passengers!).ChildRules(passenger =>
        {
            passenger.RuleFor(x => x.FullName)
                .NotEmpty()
                .WithMessage("Họ tên hành khách là bắt buộc.")
                .MaximumLength(150);
            passenger.RuleFor(x => x.BirthYear)
                .NotNull()
                .WithMessage("Năm sinh là bắt buộc cho tất cả hành khách charter.");
            passenger.RuleFor(x => x.BirthYear)
                .InclusiveBetween(1900, DateTime.UtcNow.Year)
                .WithMessage("Năm sinh không hợp lệ.")
                .When(x => x.BirthYear.HasValue);
        });

        // Validate tuổi hành khách (phải >= 12 tuổi mới là người lớn)
        RuleFor(x => x)
            .Must(x => ValidatePassengerAges(x) == null)
            .WithMessage(x => ValidatePassengerAges(x) ?? string.Empty)
            .When(x => HasPassengers(x));

        // Validate: nếu có AdultCount/ChildCount thì số lượng hành khách theo tuổi phải khớp
        RuleFor(x => x)
            .Must(x => ValidatePassengerCountMatchesAgeGroups(x) == null)
            .WithMessage(x => ValidatePassengerCountMatchesAgeGroups(x) ?? string.Empty)
            .When(x => HasPassengers(x) && (x.AdultCount.HasValue || x.ChildCount.HasValue));

        // Validate: nếu có trẻ em thì phải có ít nhất 1 người lớn
        RuleFor(x => x)
            .Must(x => ValidateAdultRequiredWhenChildExists(x) == null)
            .WithMessage(x => ValidateAdultRequiredWhenChildExists(x) ?? string.Empty)
            .When(x => HasPassengers(x));

        // Validate AdultCount/ChildCount khi không có Passengers
        RuleFor(x => x.AdultCount!.Value).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000)
            .When(x => !HasPassengers(x) && x.AdultCount.HasValue)
            .WithMessage("Số người lớn phải từ 0 đến 1000.");
        RuleFor(x => x.ChildCount!.Value).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000)
            .When(x => !HasPassengers(x) && x.ChildCount.HasValue)
            .WithMessage("Số trẻ em phải từ 0 đến 1000.");
        RuleFor(x => x)
            .Must(x => x.AdultCount!.Value + x.ChildCount!.Value >= 1)
            .When(x => !HasPassengers(x))
            .WithMessage("Booking phải có ít nhất 1 hành khách (người lớn hoặc trẻ em).");
        RuleFor(x => x)
            .Must(x => !(x.ChildCount!.Value > 0 && x.AdultCount!.Value == 0))
            .When(x => !HasPassengers(x))
            .WithMessage("Khi có trẻ em đi cùng phải có ít nhất 1 người lớn.");
        RuleFor(x => x)
            .Must(x => x.AdultCount!.Value + x.ChildCount!.Value <= 1000)
            .When(x => !HasPassengers(x))
            .WithMessage("Tổng số khách không được vượt quá 1000.");

        RuleFor(x => x.RequestedBoats)
            .Must(x => x is null || x.Count > 0)
            .WithMessage("Danh sách tàu yêu cầu không được rỗng nếu được gửi.");
        RuleFor(x => x.RequestedBoats)
            .Must(x => x is null || x.Count <= CharterBookingBoatSelectionSupport.MaxRequestedBoatCount)
            .WithMessage($"Số lượng tàu yêu cầu không được vượt quá {CharterBookingBoatSelectionSupport.MaxRequestedBoatCount}.");
        RuleForEach(x => x.RequestedBoats).ChildRules(boat =>
        {
            boat.RuleFor(x => x.NumberOfDecks)
                .GreaterThan(0)
                .WithMessage("Số tầng tàu yêu cầu phải lớn hơn 0.");
        });
        RuleFor(x => x.SpecialRequests).MaximumLength(1000).When(x => x.SpecialRequests is not null);
        RuleFor(x => x.ContactName)
            .NotEmpty()
            .WithMessage("Họ tên người đặt là bắt buộc.")
            .MaximumLength(150)
            .Must(BeValidContactName)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactName))
            .WithMessage("Họ tên chỉ được chứa chữ cái và khoảng trắng, không chứa số hoặc ký tự đặc biệt.");
        RuleFor(x => x.ContactPhone)
            .MaximumLength(30)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone));
        RuleFor(x => x.ContactPhone)
            .Must(BeValidVietnamMobilePhone)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPhone))
            .WithMessage("Số điện thoại phải gồm đúng 10 chữ số và bắt đầu bằng 0[3|5|7|8|9].");
        RuleFor(x => x.ContactEmail)
            .MaximumLength(255)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("Email nhận thông tin charter booking không hợp lệ.");
        RuleFor(x => x.ContactEmail)
            .Must(BeAllowedCharterEmailDomain)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail))
            .WithMessage("Email chỉ chấp nhận đuôi @gmail.com hoặc @fpt.edu.vn.");
        RuleFor(x => x.InsurancePackageId)
            .NotEmpty()
            .When(x => x.InsurancePackageId.HasValue)
            .WithMessage("Gói bảo hiểm không hợp lệ.");
        RuleFor(x => x.InsurancePackageId)
            .NotNull()
            .When(x => x.InsuranceSelected == true)
            .WithMessage("Vui lòng chọn gói bảo hiểm.");
        RuleFor(x => x.FromStationId)
            .NotNull()
            .WithMessage("Bến bắt đầu là bắt buộc.");
        RuleFor(x => x.ToStationId).NotEqual(x => x.FromStationId)
            .When(x => x.FromStationId.HasValue
                && x.ToStationId.HasValue
                && !HasItineraryStops(x.ItineraryStops))
            .WithMessage("Bến đón và bến trả phải khác nhau khi không có điểm dừng.");
        RuleFor(x => x.ItineraryStops)
            .Must(stops => stops is null || stops.Count <= 50)
            .WithMessage("Hành trình không được vượt quá 50 điểm dừng.");
        RuleFor(x => x.ItineraryStops)
            .Must(HaveUniqueStopOrders)
            .When(x => x.ItineraryStops is { Count: > 0 })
            .WithMessage("Thứ tự điểm dừng không được trùng nhau.");
        RuleForEach(x => x.ItineraryStops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StationId).NotEqual(Guid.Empty).WithMessage("StationId của điểm dừng không hợp lệ.");
            stop.RuleFor(x => x.StopOrder).GreaterThan(0).WithMessage("StopOrder phải lớn hơn 0.");
            stop.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 1440)
                .WithMessage("Thời gian dừng phải từ 0 đến 1440 phút.");
            stop.RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
        });
    }

    private static bool HasPassengers(CreateCharterBookingCommand x) =>
        x.Passengers is { Count: > 0 };

    private static string? ValidatePassengerCount(CreateCharterBookingCommand x)
    {
        if (!HasPassengers(x)) return null;
        var count = x.Passengers!.Count;
        if (count > 1000) return "Tổng số hành khách không được vượt quá 1000.";
        return null;
    }

    private static string? ValidatePassengerAges(CreateCharterBookingCommand x)
    {
        if (!HasPassengers(x) || x.Passengers is not { Count: > 0 }) return null;
        var currentYear = DateTime.UtcNow.Year;
        foreach (var p in x.Passengers!)
        {
            if (!p.BirthYear.HasValue) continue;
            var age = currentYear - p.BirthYear.Value;
            if (age < 0) return $"Năm sinh {p.BirthYear} không hợp lệ (lớn hơn năm hiện tại).";
        }
        return null;
    }

    private static string? ValidatePassengerCountMatchesAgeGroups(CreateCharterBookingCommand x)
    {
        if (!HasPassengers(x) || x.Passengers is not { Count: > 0 }) return null;

        var currentYear = DateTime.UtcNow.Year;
        var actualAdultCount = 0;
        var actualChildCount = 0;

        foreach (var p in x.Passengers!)
        {
            if (!p.BirthYear.HasValue) continue;
            var age = currentYear - p.BirthYear.Value;
            if (age >= AdultMinimumAge)
                actualAdultCount++;
            else
                actualChildCount++;
        }

        if (x.AdultCount.HasValue && actualAdultCount != x.AdultCount.Value)
            return $"Số người lớn không khớp: khai báo {x.AdultCount.Value} nhưng theo năm sinh có {actualAdultCount} người lớn.";

        if (x.ChildCount.HasValue && actualChildCount != x.ChildCount.Value)
            return $"Số trẻ em không khớp: khai báo {x.ChildCount.Value} nhưng theo năm sinh có {actualChildCount} trẻ em.";

        return null;
    }

    private static string? ValidateAdultRequiredWhenChildExists(CreateCharterBookingCommand x)
    {
        if (!HasPassengers(x) || x.Passengers is not { Count: > 0 }) return null;

        var currentYear = DateTime.UtcNow.Year;
        var hasAdult = false;
        var hasChild = false;

        foreach (var p in x.Passengers!)
        {
            if (!p.BirthYear.HasValue) continue;
            var age = currentYear - p.BirthYear.Value;
            if (age >= AdultMinimumAge)
                hasAdult = true;
            else
                hasChild = true;
        }

        if (hasChild && !hasAdult)
            return "Danh sách hành khách phải có ít nhất 1 người lớn khi có trẻ em đi cùng.";

        return null;
    }

    private static bool HaveUniqueStopOrders(IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops) =>
        stops is null || stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count;

    private static bool HasItineraryStops(IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops) =>
        stops is { Count: > 0 };

    private static readonly TimeOnly CharterStartWindowBegin = new(7, 0);
    private static readonly TimeOnly CharterStartWindowEnd = new(22, 0);

    private static bool BeWithinCharterStartTimeWindow(TimeOnly startTime) =>
        startTime >= CharterStartWindowBegin && startTime <= CharterStartWindowEnd;

    // Họ tên: chỉ chữ cái Unicode (bao gồm tiếng Việt có dấu) + khoảng trắng, không số / ký tự đặc biệt.
    // Trim trước khi check, chỉ chặn khoảng trắng ở giữa nhiều lần liên tiếp để tránh "  ".
    private static readonly Regex ContactNameRegex =
        new(@"^[\p{L}][\p{L}\s]*$", RegexOptions.Compiled);

    private static bool BeValidContactName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && ContactNameRegex.IsMatch(name.Trim())
        && !name.Trim().Contains("  ", StringComparison.Ordinal);

    // Số di động VN: bắt đầu bằng 0, tiếp theo là 3/5/7/8/9, còn lại là 8 chữ số (tổng 10 số).
    private static readonly Regex VietnamMobilePhoneRegex =
        new(@"^0[35789]\d{8}$", RegexOptions.Compiled);

    private static bool BeValidVietnamMobilePhone(string? phone) =>
        !string.IsNullOrWhiteSpace(phone) && VietnamMobilePhoneRegex.IsMatch(phone.Trim());

    private static readonly string[] AllowedCharterEmailDomains = ["gmail.com", "fpt.edu.vn"];

    private static bool BeAllowedCharterEmailDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        var domain = email[(atIndex + 1)..].Trim().ToLowerInvariant();
        return AllowedCharterEmailDomains.Contains(domain);
    }
}

public sealed class CreateCharterBookingCommandHandler
    : IRequestHandler<CreateCharterBookingCommand, CreateCharterBookingResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly IBookingCodeGenerator _bookingCodeGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;
    private readonly INotificationRealtimeNotifier _notificationRealtimeNotifier;

    public CreateCharterBookingCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        IBookingCodeGenerator bookingCodeGenerator,
        TimeProvider timeProvider,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null,
        INotificationRealtimeNotifier? notificationRealtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _bookingCodeGenerator = bookingCodeGenerator;
        _timeProvider = timeProvider;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
        _notificationRealtimeNotifier = notificationRealtimeNotifier ?? NullNotificationRealtimeNotifier.Instance;
    }

    public async Task<CreateCharterBookingResult> Handle(
        CreateCharterBookingCommand request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var minimumDepartureDate = today.AddDays(7);
        if (request.DepartureDate < minimumDepartureDate)
            throw new ValidationException([new ValidationFailure(nameof(request.DepartureDate),
                $"Charter booking phải được đặt trước ít nhất 7 ngày. Ngày khởi hành sớm nhất là {minimumDepartureDate:dd/MM/yyyy}.")]);

        // Tính số lượng hành khách và phân loại Adult/Child
        var (adultCount, childCount, passengerCount, passengersToCreate) = ResolvePassengerCounts(request, now);

        const decimal subtotal = 0;
        var requestedBoatDecks = CharterBookingBoatSelectionSupport.NormalizeRequestedBoatDecks(
            request.RequestedBoats);
        var requestedBoatCount = CharterBookingBoatSelectionSupport.ResolveRequestedBoatCount(requestedBoatDecks);
        var requestedBoatDeckStorage = CharterBookingBoatSelectionSupport.ToStorageValue(requestedBoatDecks);

        EnsureRouteEndpointCombinationValid(
            request.FromStationId,
            request.ToStationId,
            request.ItineraryStops is { Count: > 0 },
            nameof(request.ToStationId));
        await CharterBookingStationValidationSupport.EnsureWaterbusDepartureStationAsync(
            _context,
            request.FromStationId,
            nameof(request.FromStationId),
            cancellationToken);
        await EnsureStationExistsAsync(request.ToStationId, nameof(request.ToStationId), cancellationToken);
        await EnsureItineraryStationsExistAsync(request.ItineraryStops, cancellationToken);

        const decimal discount = 0;
        var total = subtotal - discount;

        var user = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("userId", "User không tồn tại.")]);
        var contactName = ResolveRequiredContactValue(
            request.ContactName,
            user.FullName,
            nameof(request.ContactName),
            "Họ tên người đặt là bắt buộc.");
        var contactPhone = ResolveRequiredContactValue(
            request.ContactPhone,
            user.PhoneNumber,
            nameof(request.ContactPhone),
            "Số điện thoại người đặt là bắt buộc.");
        var contactEmail = ResolveRequiredContactValue(
            request.ContactEmail,
            user.Email,
            nameof(request.ContactEmail),
            "Email nhận thông tin charter booking là bắt buộc.");

        await CharterBookingDuplicateSupport.EnsureNoDuplicateActiveRequestAsync(
            _context,
            userId,
            excludeBookingId: null,
            request.DepartureDate,
            request.StartTime,
            request.RentalUnit,
            request.DurationValue,
            request.FromStationId,
            request.ToStationId,
            adultCount,
            childCount,
            requestedBoatDeckStorage,
            CharterBookingDuplicateSupport.ToItineraryStops(request.ItineraryStops),
            contactPhone,
            contactEmail,
            cancellationToken);
        BookingInsuranceSnapshot? insuranceSnapshot;
        if (passengerCount < 1)
        {
            insuranceSnapshot = null;
        }
        else
        {
            insuranceSnapshot = await CharterBookingInsuranceSupport.ResolveRequestedInsuranceSnapshotAsync(
                _context,
                request.InsuranceSelected,
                request.InsurancePackageId,
                currentSnapshot: null,
                insuredPassengerQuantity: passengerCount,
                now,
                cancellationToken);
        }

        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            FromStationId = request.FromStationId,
            ToStationId = request.ToStationId,
            DepartureDate = request.DepartureDate,
            StartTime = request.StartTime,
            RentalUnit = request.RentalUnit,
            DurationValue = request.DurationValue,
            PassengerCount = passengerCount,
            AdultCount = adultCount,
            ChildCount = childCount,
            RequestedBoatCount = requestedBoatCount == 0 ? null : requestedBoatCount,
            RequestedBoatDecks = requestedBoatDeckStorage,
            RequestedBoatTypes = null,
            PreferredSeatSetupType = null,
            SpecialRequests = request.SpecialRequests?.Trim(),
            BookingCode = ToCharterBookingCode(await _bookingCodeGenerator.GenerateAsync(cancellationToken)),
            ContactName = contactName,
            ContactPhone = contactPhone,
            ContactEmail = contactEmail,
            BookingStatus = BookingStatus.PendingQuote,
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            TotalAmount = total,
            RemainingAmount = total,
            InsuranceSnapshot = insuranceSnapshot,
            ItineraryStops = request.ItineraryStops?
                .OrderBy(x => x.StopOrder)
                .Select(x => new BookingItineraryStop
                {
                    StationId = x.StationId,
                    StopOrder = x.StopOrder,
                    StayDurationMinutes = x.StayDurationMinutes,
                    Note = x.Note?.Trim()
                })
                .ToList() ?? [],
            Passengers = passengersToCreate
        };

        _context.Set<Booking>().Add(booking);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.DepartureDate),
                "Tạo yêu cầu thuê tàu thất bại. Vui lòng thử lại.")]);
        }

        var requestedNotifications = await NotificationSupport.AddCharterBookingRequestedNotificationsAsync(
            _context,
            booking,
            now,
            cancellationToken);
        if (requestedNotifications.Count > 0)
        {
            await NotificationSupport.PublishCreatedAsync(
                _notificationRealtimeNotifier,
                requestedNotifications,
                cancellationToken);
        }

        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "Created",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus,
                _timeProvider.GetUtcNow()),
            cancellationToken);

        return new CreateCharterBookingResult(
            booking.Id,
            booking.BookingCode,
            null,
            booking.SubtotalAmount,
            booking.DiscountAmount,
            booking.TotalAmount,
            booking.BookingStatus.ToString(),
            0,
            requestedBoatCount,
            CharterBookingBoatSelectionSupport.ToDtos(requestedBoatDecks));
    }

    private async Task EnsureStationExistsAsync(Guid? stationId, string field, CancellationToken cancellationToken)
    {
        if (stationId is null)
            return;

        var exists = await _context.Set<Station>()
            .AnyAsync(s => s.Id == stationId.Value, cancellationToken);
        if (!exists)
            throw new ValidationException([new ValidationFailure(field, "Bến không tồn tại.")]);
    }

    private static void EnsureRouteEndpointCombinationValid(
        Guid? fromStationId,
        Guid? toStationId,
        bool hasItineraryStops,
        string field)
    {
        if (fromStationId.HasValue
            && toStationId.HasValue
            && fromStationId.Value == toStationId.Value
            && !hasItineraryStops)
        {
            throw new ValidationException([new ValidationFailure(field,
                "Bến đón và bến trả phải khác nhau khi không có điểm dừng.")]);
        }
    }

    private async Task EnsureItineraryStationsExistAsync(
        IReadOnlyList<CreateCharterBookingItineraryStopRequest>? stops,
        CancellationToken cancellationToken)
    {
        if (stops is null || stops.Count == 0)
            return;

        var stationIds = stops.Select(x => x.StationId).Distinct().ToArray();
        var existingStationIds = await _context.Set<Station>()
            .Where(s => stationIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var missingStationId = stationIds.Except(existingStationIds).FirstOrDefault();
        if (missingStationId != Guid.Empty)
            throw new ValidationException([new ValidationFailure(nameof(CreateCharterBookingCommand.ItineraryStops),
                $"Điểm dừng có stationId '{missingStationId}' không tồn tại.")]);
    }

    private static string ToCharterBookingCode(string bookingCode)
    {
        if (bookingCode.StartsWith("CB", StringComparison.OrdinalIgnoreCase))
        {
            return bookingCode;
        }

        if (bookingCode.StartsWith("BK-", StringComparison.OrdinalIgnoreCase))
        {
            return $"CB-{bookingCode[3..]}";
        }

        if (bookingCode.StartsWith("BK", StringComparison.OrdinalIgnoreCase))
        {
            return $"CB{bookingCode[2..]}";
        }

        return $"CB-{bookingCode}";
    }

    private static string ResolveRequiredContactValue(
        string? requestedValue,
        string? fallbackValue,
        string field,
        string message)
    {
        var value = NormalizeContactValue(requestedValue) ?? NormalizeContactValue(fallbackValue);
        if (value is null)
        {
            throw new ValidationException([new ValidationFailure(field, message)]);
        }

        return value;
    }

    private static string? NormalizeContactValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private (int adultCount, int childCount, int totalCount, List<BookingPassenger> passengers) ResolvePassengerCounts(
        CreateCharterBookingCommand request, DateTimeOffset now)
    {
        // Nếu có danh sách hành khách
        if (request.Passengers is { Count: > 0 })
        {
            var passengers = new List<BookingPassenger>();
            var adultCount = 0;
            var childCount = 0;
            var today = DateOnly.FromDateTime(now.UtcDateTime);

            foreach (var p in request.Passengers)
            {
                // Ưu tiên BirthYear từ request, nếu không có thì parse từ DateOfBirth
                int? birthYear = p.BirthYear;
                if (!birthYear.HasValue && !string.IsNullOrWhiteSpace(p.DateOfBirth))
                {
                    if (CharterBookingPassengerSupport.TryParseDateOfBirth(p.DateOfBirth, out var dob))
                    {
                        birthYear = dob.Year;
                    }
                    else if (CharterBookingPassengerSupport.TryParseBirthYear(p.DateOfBirth, out var parsedYear))
                    {
                        birthYear = parsedYear;
                    }
                }

                var passengerType = birthYear.HasValue
                    ? CharterBookingPassengerSupport.ResolvePassengerType(birthYear.Value, today)
                    : CharterBookingPassengerType.Adult.ToString();

                if (string.Equals(passengerType, CharterBookingPassengerType.Adult.ToString(), StringComparison.OrdinalIgnoreCase))
                    adultCount++;
                else
                    childCount++;

                passengers.Add(new BookingPassenger
                {
                    FullName = p.FullName.Trim(),
                    BirthYear = birthYear,
                    PassengerType = passengerType,
                    ApprovalStatus = CharterBookingPassengerSupport.ApprovalStatusApproved,
                    RequestedAt = now,
                    RequestedByUserId = _userContext.UserId
                });
            }

            return (adultCount, childCount, passengers.Count, passengers);
        }

        // Nếu không có danh sách, dùng AdultCount/ChildCount
        var totalAdult = request.AdultCount ?? 0;
        var totalChild = request.ChildCount ?? 0;
        return (totalAdult, totalChild, totalAdult + totalChild, new List<BookingPassenger>());
    }
}

public sealed class CharterBookingPassengerRequestValidator : AbstractValidator<CharterBookingPassengerRequest>
{
    public CharterBookingPassengerRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("fullName is required.")
            .MaximumLength(150);
        RuleFor(x => x.DateOfBirth)
            .Must(x => string.IsNullOrWhiteSpace(x)
                || CharterBookingPassengerSupport.TryParseBirthYear(x, out _)
                || CharterBookingPassengerSupport.TryParseDateOfBirth(x, out _))
            .WithMessage("Năm sinh/ngày sinh không hợp lệ. Dùng năm yyyy hoặc ngày yyyy-MM-dd/dd/MM/yyyy.");
        RuleFor(x => x.DateOfBirth)
            .Must(x => !CharterBookingPassengerSupport.TryParseBirthYear(x, out var birthYear)
                || birthYear <= DateTime.UtcNow.Year)
            .WithMessage("Năm sinh không được ở tương lai.");
        RuleFor(x => x.DateOfBirth)
            .Must(x => CharterBookingPassengerSupport.TryParseBirthYear(x, out _)
                || !CharterBookingPassengerSupport.TryParseDateOfBirth(x, out var dateOfBirth)
                || dateOfBirth <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Ngày sinh không được ở tương lai.");
        RuleFor(x => x.BirthYear)
            .Must(x => !x.HasValue
                || CharterBookingPassengerSupport.IsValidBirthYear(x.Value, DateOnly.FromDateTime(DateTime.UtcNow)))
            .WithMessage("Năm sinh không hợp lệ hoặc ở tương lai.");
    }
}
