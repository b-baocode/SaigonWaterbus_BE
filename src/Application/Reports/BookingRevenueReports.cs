using System.Linq.Expressions;
using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Reports;

public sealed record GetRevenueReportQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    string? ServiceType = null,
    string? PaymentMethod = null,
    Guid? SoldByStaffId = null) : IRequest<RevenueReportDto>;

public sealed record RevenueReportDto(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal GrossRevenue,
    decimal RefundAmount,
    decimal NetRevenue,
    int PaidPaymentCount,
    int BookingCount,
    int TicketCount,
    int CounterBookingCount,
    IReadOnlyList<RevenueBreakdownDto> ByPaymentMethod,
    IReadOnlyList<RevenueBreakdownDto> ByServiceType,
    IReadOnlyList<DailyRevenueDto> Daily);

public sealed record RevenueBreakdownDto(
    string Key,
    int PaymentCount,
    int BookingCount,
    decimal GrossRevenue,
    decimal RefundAmount,
    decimal NetRevenue);

public sealed record DailyRevenueDto(
    DateOnly Date,
    int PaymentCount,
    int BookingCount,
    decimal GrossRevenue,
    decimal RefundAmount,
    decimal NetRevenue);

public sealed record GetBookingManagementListQuery(
    string? Keyword = null,
    string? BookingStatus = null,
    string? PaymentStatus = null,
    string? ServiceType = null,
    string? PaymentMethod = null,
    Guid? SoldByStaffId = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    DateTimeOffset? DepartureFrom = null,
    DateTimeOffset? DepartureTo = null,
    int Page = 1,
    int PageSize = 20) : IRequest<BookingManagementListDto>;

public sealed record BookingManagementListDto(
    int TotalCount,
    int Page,
    int PageSize,
    BookingManagementSummaryDto Summary,
    IReadOnlyList<BookingManagementItemDto> Items);

public sealed record BookingManagementSummaryDto(
    int TotalBookings,
    int PendingPaymentCount,
    int ConfirmedCount,
    int CompletedCount,
    int CancelledCount,
    int ExpiredCount,
    int CounterBookingCount,
    int OnlineBookingCount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RemainingAmount);

public sealed record BookingManagementItemDto(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset BookedAt,
    string BookingType,
    string ServiceType,
    string? RouteType,
    string BookingStatus,
    string PaymentStatus,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    int PassengerCount,
    int TicketCount,
    string ContactName,
    string? ContactPhone,
    string? ContactEmail,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerEmail,
    Guid? SoldByStaffId,
    string? SoldByStaffName,
    string? LatestPaymentMethod,
    DateTimeOffset? LatestPaidAt,
    string? TripCode,
    string? ReturnTripCode,
    string? RouteName,
    DateTimeOffset? DepartureAt,
    DateTimeOffset? ArrivalAt);

public sealed record GetBookingSelectOptionsQuery(
    string? Keyword = null,
    string? BookingStatus = null,
    string? PaymentStatus = null,
    string? ServiceType = null,
    int Limit = 20) : IRequest<IReadOnlyList<BookingSelectOptionDto>>;

public sealed record BookingSelectOptionDto(
    Guid BookingId,
    string BookingCode,
    string Label,
    string ContactName,
    string BookingStatus,
    string PaymentStatus,
    string ServiceType,
    decimal TotalAmount,
    DateTimeOffset? DepartureAt);

public sealed class GetRevenueReportQueryValidator : AbstractValidator<GetRevenueReportQuery>
{
    public GetRevenueReportQueryValidator()
    {
        RuleFor(x => x.From).LessThan(x => x.To)
            .WithMessage("fromDate phải nhỏ hơn toDate.");
        RuleFor(x => x)
            .Must(x => x.To - x.From <= TimeSpan.FromDays(366))
            .WithMessage("Khoảng báo cáo doanh thu không được vượt quá 366 ngày.");
    }
}

public sealed class GetBookingManagementListQueryValidator : AbstractValidator<GetBookingManagementListQuery>
{
    public GetBookingManagementListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.CreatedFrom)
            .LessThan(x => x.CreatedTo)
            .When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue)
            .WithMessage("createdFrom phải nhỏ hơn createdTo.");
        RuleFor(x => x.DepartureFrom)
            .LessThan(x => x.DepartureTo)
            .When(x => x.DepartureFrom.HasValue && x.DepartureTo.HasValue)
            .WithMessage("departureFrom phải nhỏ hơn departureTo.");
    }
}

public sealed class GetBookingSelectOptionsQueryValidator : AbstractValidator<GetBookingSelectOptionsQuery>
{
    public GetBookingSelectOptionsQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class GetRevenueReportQueryHandler
    : IRequestHandler<GetRevenueReportQuery, RevenueReportDto>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetRevenueReportQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<RevenueReportDto> Handle(
        GetRevenueReportQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        BookingReportQuerySupport.EnsureCanViewRevenue(actor);

        var serviceType = BookingReportQuerySupport.NormalizeServiceType(request.ServiceType, nameof(request.ServiceType));
        var paymentMethod = BookingReportQuerySupport.NormalizePaymentMethod(request.PaymentMethod);

        var query = _context.Set<Payment>()
            .AsNoTracking()
            .Where(p =>
                p.PaidAt.HasValue
                && p.PaidAt.Value >= request.From
                && p.PaidAt.Value < request.To
                && p.PaymentStatus == PaymentSupport.PaidStatus
                && (p.Provider == PaymentSupport.PayOsProvider
                    || p.Provider == PaymentSupport.CounterProvider
                    || p.Provider == PaymentSupport.FreeProvider));

        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            query = query.Where(p => p.PaymentMethod == paymentMethod);
        }

        if (request.SoldByStaffId.HasValue)
        {
            query = query.Where(p => p.Booking.SoldByStaffId == request.SoldByStaffId.Value);
        }

        query = BookingReportQuerySupport.ApplyServiceTypeFilter(query, serviceType);

        var rows = await query
            .Select(p => new RevenuePaymentRow(
                p.Id,
                p.BookingId,
                p.PaidAt!.Value,
                p.Amount,
                p.RefundAmount,
                p.PaymentMethod,
                p.Booking.BookingType,
                p.Booking.Trip != null ? p.Booking.Trip.Route.RouteType : null,
                p.Booking.SoldByStaffId,
                p.Booking.Tickets.Count(t => t.TicketStatus != TicketStatus.Cancelled)))
            .ToListAsync(cancellationToken);

        var grossRevenue = rows.Sum(x => x.Amount);
        var refundAmount = rows.Sum(x => x.RefundAmount);
        var bookingGroups = rows.GroupBy(x => x.BookingId).ToArray();

        return new RevenueReportDto(
            request.From,
            request.To,
            grossRevenue,
            refundAmount,
            grossRevenue - refundAmount,
            rows.Count,
            bookingGroups.Length,
            bookingGroups.Sum(g => g.Max(x => x.TicketCount)),
            bookingGroups.Count(g => g.Any(x => x.SoldByStaffId.HasValue)),
            BuildBreakdown(rows, x => x.PaymentMethod),
            BuildBreakdown(rows, x => BookingReportQuerySupport.ResolveServiceType(x.BookingType, x.RouteType)),
            BuildDaily(rows));
    }

    private static IReadOnlyList<RevenueBreakdownDto> BuildBreakdown(
        IReadOnlyList<RevenuePaymentRow> rows,
        Func<RevenuePaymentRow, string> keySelector) =>
        rows
            .GroupBy(keySelector)
            .OrderBy(x => x.Key)
            .Select(x => new RevenueBreakdownDto(
                x.Key,
                x.Count(),
                x.Select(r => r.BookingId).Distinct().Count(),
                x.Sum(r => r.Amount),
                x.Sum(r => r.RefundAmount),
                x.Sum(r => r.Amount - r.RefundAmount)))
            .ToArray();

    private static IReadOnlyList<DailyRevenueDto> BuildDaily(IReadOnlyList<RevenuePaymentRow> rows) =>
        rows
            .GroupBy(x => DateOnly.FromDateTime(x.PaidAt.ToOffset(VietnamOffset).DateTime))
            .OrderBy(x => x.Key)
            .Select(x => new DailyRevenueDto(
                x.Key,
                x.Count(),
                x.Select(r => r.BookingId).Distinct().Count(),
                x.Sum(r => r.Amount),
                x.Sum(r => r.RefundAmount),
                x.Sum(r => r.Amount - r.RefundAmount)))
            .ToArray();
}

public sealed class GetBookingManagementListQueryHandler
    : IRequestHandler<GetBookingManagementListQuery, BookingManagementListDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingManagementListQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<BookingManagementListDto> Handle(
        GetBookingManagementListQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = BookingReportQuerySupport.BuildFilteredBookingQuery(_context, request, actor);

        var totalCount = await query.CountAsync(cancellationToken);
        var summary = await BuildSummaryAsync(query, cancellationToken);
        var rows = await BookingReportQuerySupport.ApplyDefaultOrdering(query)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(BookingReportQuerySupport.ManagementProjection)
            .ToListAsync(cancellationToken);

        return new BookingManagementListDto(
            totalCount,
            request.Page,
            request.PageSize,
            summary,
            rows.Select(BookingReportQuerySupport.ToManagementItemDto).ToArray());
    }

    private async Task<BookingManagementSummaryDto> BuildSummaryAsync(
        IQueryable<Booking> query,
        CancellationToken cancellationToken)
    {
        var filteredBookingIds = query.Select(b => b.Id);
        var paidPayments = _context.Set<Payment>()
            .AsNoTracking()
            .Where(p =>
                filteredBookingIds.Contains(p.BookingId)
                && p.PaymentStatus == PaymentSupport.PaidStatus
                && (p.Provider == PaymentSupport.PayOsProvider
                    || p.Provider == PaymentSupport.CounterProvider
                    || p.Provider == PaymentSupport.FreeProvider));

        var totalBookings = await query.CountAsync(cancellationToken);
        var pendingPaymentCount = await query.CountAsync(b => b.BookingStatus == BookingStatus.PendingPayment, cancellationToken);
        var confirmedCount = await query.CountAsync(b => b.BookingStatus == BookingStatus.Confirmed, cancellationToken);
        var completedCount = await query.CountAsync(b => b.BookingStatus == BookingStatus.Completed, cancellationToken);
        var cancelledCount = await query.CountAsync(b => b.BookingStatus == BookingStatus.Cancelled, cancellationToken);
        var expiredCount = await query.CountAsync(b => b.BookingStatus == BookingStatus.Expired, cancellationToken);
        var counterBookingCount = await query.CountAsync(b => b.SoldByStaffId.HasValue, cancellationToken);
        var totalAmount = await query.SumAsync(b => (decimal?)b.TotalAmount, cancellationToken) ?? 0m;
        var remainingAmount = await query.SumAsync(b => (decimal?)b.RemainingAmount, cancellationToken) ?? 0m;
        var paidAmount = await paidPayments.SumAsync(p => (decimal?)(p.Amount - p.RefundAmount), cancellationToken) ?? 0m;

        return new BookingManagementSummaryDto(
            totalBookings,
            pendingPaymentCount,
            confirmedCount,
            completedCount,
            cancelledCount,
            expiredCount,
            counterBookingCount,
            totalBookings - counterBookingCount,
            totalAmount,
            paidAmount,
            remainingAmount);
    }
}

public sealed class GetBookingSelectOptionsQueryHandler
    : IRequestHandler<GetBookingSelectOptionsQuery, IReadOnlyList<BookingSelectOptionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetBookingSelectOptionsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyList<BookingSelectOptionDto>> Handle(
        GetBookingSelectOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        BookingReportQuerySupport.EnsureCanViewBookingManagement(actor);

        var listQuery = new GetBookingManagementListQuery(
            request.Keyword,
            request.BookingStatus,
            request.PaymentStatus,
            request.ServiceType,
            PaymentMethod: null,
            SoldByStaffId: null,
            CreatedFrom: null,
            CreatedTo: null,
            DepartureFrom: null,
            DepartureTo: null,
            Page: 1,
            PageSize: request.Limit);
        var query = BookingReportQuerySupport.BuildFilteredBookingQuery(_context, listQuery, actor);

        var rows = await BookingReportQuerySupport.ApplyDefaultOrdering(query)
            .Take(request.Limit)
            .Select(BookingReportQuerySupport.ManagementProjection)
            .ToListAsync(cancellationToken);

        return rows.Select(BookingReportQuerySupport.ToSelectOptionDto).ToArray();
    }
}

internal sealed record RevenuePaymentRow(
    Guid PaymentId,
    Guid BookingId,
    DateTimeOffset PaidAt,
    decimal Amount,
    decimal RefundAmount,
    string PaymentMethod,
    string BookingType,
    string? RouteType,
    Guid? SoldByStaffId,
    int TicketCount);

internal sealed record BookingManagementRow(
    Guid BookingId,
    string BookingCode,
    DateTimeOffset BookedAt,
    string BookingType,
    BookingStatus BookingStatus,
    string PaymentStatus,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    int PassengerCount,
    int TicketCount,
    string ContactName,
    string? ContactPhone,
    string? ContactEmail,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerEmail,
    Guid? SoldByStaffId,
    string? SoldByStaffName,
    string? LatestPaymentMethod,
    DateTimeOffset? LatestPaidAt,
    string? TripCode,
    string? ReturnTripCode,
    string? RouteName,
    string? RouteType,
    DateTimeOffset? SeatDepartureAt,
    DateTimeOffset? SeatArrivalAt,
    DateOnly? CharterDepartureDate,
    TimeOnly? CharterStartTime,
    int? CharterDurationValue,
    BoatRentalUnit? CharterRentalUnit);

internal static class BookingReportQuerySupport
{
    private const string All = "All";

    public static readonly Expression<Func<Booking, BookingManagementRow>> ManagementProjection = b =>
        new BookingManagementRow(
            b.Id,
            b.BookingCode,
            b.Created,
            b.BookingType,
            b.BookingStatus,
            b.PaymentStatus,
            b.SubtotalAmount,
            b.DiscountAmount,
            b.TotalAmount,
            b.Payments
                .Where(p => p.PaymentStatus == PaymentSupport.PaidStatus
                    && (p.Provider == PaymentSupport.PayOsProvider
                        || p.Provider == PaymentSupport.CounterProvider
                        || p.Provider == PaymentSupport.FreeProvider))
                .Sum(p => (decimal?)(p.Amount - p.RefundAmount)) ?? 0m,
            b.RemainingAmount,
            b.Passengers.Count,
            b.Tickets.Count(t => t.TicketStatus != TicketStatus.Cancelled),
            b.ContactName,
            b.ContactPhone,
            b.ContactEmail,
            b.UserId,
            b.User != null ? b.User.FullName : null,
            b.User != null ? b.User.Email : null,
            b.SoldByStaffId,
            b.SoldByStaff != null ? b.SoldByStaff.FullName : null,
            b.Payments
                .Where(p => p.PaymentStatus == PaymentSupport.PaidStatus
                    && (p.Provider == PaymentSupport.PayOsProvider
                        || p.Provider == PaymentSupport.CounterProvider
                        || p.Provider == PaymentSupport.FreeProvider))
                .OrderByDescending(p => p.PaidAt)
                .Select(p => p.PaymentMethod)
                .FirstOrDefault(),
            b.Payments
                .Where(p => p.PaymentStatus == PaymentSupport.PaidStatus
                    && (p.Provider == PaymentSupport.PayOsProvider
                        || p.Provider == PaymentSupport.CounterProvider
                        || p.Provider == PaymentSupport.FreeProvider))
                .Max(p => (DateTimeOffset?)p.PaidAt),
            b.Trip != null ? b.Trip.TripCode : null,
            b.ReturnTrip != null ? b.ReturnTrip.TripCode : null,
            b.Trip != null ? b.Trip.Route.RouteName : b.CharterRoute != null ? b.CharterRoute.RouteName : null,
            b.Trip != null ? b.Trip.Route.RouteType : null,
            b.Trip != null ? b.Trip.DepartureTime : null,
            b.Trip != null ? b.Trip.ArrivalTime : null,
            b.DepartureDate,
            b.StartTime,
            b.DurationValue,
            b.RentalUnit);

    public static void EnsureCanViewRevenue(User actor)
    {
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor))
        {
            throw new ForbiddenAccessException();
        }
    }

    public static void EnsureCanViewBookingManagement(User actor)
    {
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }
    }

    public static IQueryable<Booking> BuildFilteredBookingQuery(
        IApplicationDbContext context,
        GetBookingManagementListQuery request,
        User actor)
    {
        EnsureCanViewBookingManagement(actor);
        var query = context.Set<Booking>().AsNoTracking();

        if (AuthSupport.IsStaff(actor) && !AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor))
        {
            query = query.Where(b => b.SoldByStaffId == actor.Id);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToUpperInvariant();
            query = query.Where(b =>
                b.BookingCode.ToUpper().Contains(keyword)
                || b.ContactName.ToUpper().Contains(keyword)
                || (b.ContactPhone != null && b.ContactPhone.ToUpper().Contains(keyword))
                || (b.ContactEmail != null && b.ContactEmail.ToUpper().Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(request.BookingStatus))
        {
            if (!Enum.TryParse<BookingStatus>(request.BookingStatus.Trim(), ignoreCase: true, out var status))
            {
                throw new ValidationException([new ValidationFailure(nameof(request.BookingStatus),
                    "bookingStatus không hợp lệ.")]);
            }

            query = query.Where(b => b.BookingStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentStatus))
        {
            var paymentStatus = request.PaymentStatus.Trim().ToUpperInvariant();
            query = query.Where(b => b.PaymentStatus.ToUpper() == paymentStatus);
        }

        var serviceType = NormalizeServiceType(request.ServiceType, nameof(request.ServiceType));
        query = ApplyServiceTypeFilter(query, serviceType);

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            query = query.Where(b => b.Payments.Any(p => p.PaymentMethod == paymentMethod));
        }

        if (request.SoldByStaffId.HasValue)
        {
            query = query.Where(b => b.SoldByStaffId == request.SoldByStaffId.Value);
        }

        if (request.CreatedFrom.HasValue)
        {
            query = query.Where(b => b.Created >= request.CreatedFrom.Value);
        }

        if (request.CreatedTo.HasValue)
        {
            query = query.Where(b => b.Created < request.CreatedTo.Value);
        }

        if (request.DepartureFrom.HasValue)
        {
            var from = request.DepartureFrom.Value;
            var fromDate = DateOnly.FromDateTime(from.Date);
            query = query.Where(b =>
                (b.BookingType == Booking.SeatBookingType && b.Trip != null && b.Trip.DepartureTime >= from)
                || (b.BookingType == Booking.CharterBookingType && b.DepartureDate.HasValue && b.DepartureDate.Value >= fromDate));
        }

        if (request.DepartureTo.HasValue)
        {
            var to = request.DepartureTo.Value;
            var toDate = DateOnly.FromDateTime(to.Date);
            query = query.Where(b =>
                (b.BookingType == Booking.SeatBookingType && b.Trip != null && b.Trip.DepartureTime < to)
                || (b.BookingType == Booking.CharterBookingType && b.DepartureDate.HasValue && b.DepartureDate.Value < toDate));
        }

        return query;
    }

    public static IQueryable<Payment> ApplyServiceTypeFilter(
        IQueryable<Payment> query,
        string? serviceType) =>
        serviceType switch
        {
            BookingServiceTypes.Charter => query.Where(p => p.Booking.BookingType == Booking.CharterBookingType),
            BookingServiceTypes.Sightseeing => query.Where(p =>
                p.Booking.BookingType == Booking.SeatBookingType
                && p.Booking.Trip != null
                && p.Booking.Trip.Route.RouteType == RouteTypes.SightseeingLoop),
            BookingServiceTypes.Waterbus => query.Where(p =>
                p.Booking.BookingType == Booking.SeatBookingType
                && (p.Booking.Trip == null || p.Booking.Trip.Route.RouteType != RouteTypes.SightseeingLoop)),
            _ => query
        };

    public static IQueryable<Booking> ApplyServiceTypeFilter(
        IQueryable<Booking> query,
        string? serviceType) =>
        serviceType switch
        {
            BookingServiceTypes.Charter => query.Where(b => b.BookingType == Booking.CharterBookingType),
            BookingServiceTypes.Sightseeing => query.Where(b =>
                b.BookingType == Booking.SeatBookingType
                && b.Trip != null
                && b.Trip.Route.RouteType == RouteTypes.SightseeingLoop),
            BookingServiceTypes.Waterbus => query.Where(b =>
                b.BookingType == Booking.SeatBookingType
                && (b.Trip == null || b.Trip.Route.RouteType != RouteTypes.SightseeingLoop)),
            _ => query
        };

    public static string? NormalizeServiceType(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "all" => All,
            "booking" or "bus" or "waterbus" or "regular" => BookingServiceTypes.Waterbus,
            "sightseeing" or "sightseeingloop" or "tour" => BookingServiceTypes.Sightseeing,
            "charter" or "request" or "requestbooking" => BookingServiceTypes.Charter,
            _ => throw new ValidationException([new ValidationFailure(propertyName,
                "serviceType phải là Waterbus, Sightseeing hoặc Charter.")])
        };
    }

    public static string? NormalizePaymentMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "cash" or "tienmat" or "tien-mat" => PaymentSupport.CashPaymentMethod,
            "banktransfer" or "bank_transfer" or "bank-transfer" or "transfer" or "ck" or "chuyenkhoan" => PaymentSupport.BankTransferPaymentMethod,
            "payos" or "online" or "qr" => PaymentSupport.PayOsProvider,
            "free" => PaymentSupport.FreePaymentMethod,
            _ => value.Trim()
        };
    }

    public static IQueryable<Booking> ApplyDefaultOrdering(IQueryable<Booking> query) =>
        query
            .OrderByDescending(b => b.Created)
            .ThenByDescending(b => b.Id);

    public static BookingManagementItemDto ToManagementItemDto(BookingManagementRow row)
    {
        var serviceType = ResolveServiceType(row.BookingType, row.RouteType);
        var departureAt = ResolveDepartureAt(row);

        return new BookingManagementItemDto(
            row.BookingId,
            row.BookingCode,
            row.BookedAt,
            row.BookingType,
            serviceType,
            row.BookingType == Booking.CharterBookingType ? RouteTypes.Charter : row.RouteType,
            row.BookingStatus.ToString(),
            row.PaymentStatus,
            row.SubtotalAmount,
            row.DiscountAmount,
            row.TotalAmount,
            row.PaidAmount,
            row.RemainingAmount,
            row.PassengerCount,
            row.TicketCount,
            row.ContactName,
            row.ContactPhone,
            row.ContactEmail,
            row.CustomerId,
            row.CustomerName,
            row.CustomerEmail,
            row.SoldByStaffId,
            row.SoldByStaffName,
            row.LatestPaymentMethod,
            row.LatestPaidAt,
            row.TripCode,
            row.ReturnTripCode,
            row.RouteName,
            departureAt,
            row.SeatArrivalAt);
    }

    public static BookingSelectOptionDto ToSelectOptionDto(BookingManagementRow row)
    {
        var item = ToManagementItemDto(row);
        var label = $"{item.BookingCode} - {item.ContactName} - {item.ServiceType} - {item.PaymentStatus}";

        return new BookingSelectOptionDto(
            item.BookingId,
            item.BookingCode,
            label,
            item.ContactName,
            item.BookingStatus,
            item.PaymentStatus,
            item.ServiceType,
            item.TotalAmount,
            item.DepartureAt);
    }

    public static string ResolveServiceType(string bookingType, string? routeType) =>
        Booking.IsCharterBookingType(bookingType)
            ? BookingServiceTypes.Charter
            : BookingServiceTypes.Resolve(routeType);

    private static DateTimeOffset? ResolveDepartureAt(BookingManagementRow row)
    {
        if (row.SeatDepartureAt.HasValue)
        {
            return row.SeatDepartureAt;
        }

        if (!row.CharterDepartureDate.HasValue)
        {
            return null;
        }

        var startTime = row.CharterStartTime ?? TimeOnly.MinValue;
        return new DateTimeOffset(row.CharterDepartureDate.Value.ToDateTime(startTime), TimeSpan.Zero);
    }
}
