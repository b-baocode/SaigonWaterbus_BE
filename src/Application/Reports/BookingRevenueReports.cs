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
    Guid? SoldByStaffId = null,
    Guid? FromStationId = null,
    Guid? ToStationId = null) : IRequest<RevenueReportDto>;

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
    IReadOnlyList<StationRevenueDto> ByStation,
    IReadOnlyList<DailyRevenueDto> Daily);

public sealed record StationRevenueDto(
    Guid StationId,
    string StationName,
    string StationCode,
    int DepartureBookingCount,
    int DepartureTicketCount,
    decimal DepartureRevenue,
    decimal DepartureRefundAmount,
    decimal DepartureNetRevenue,
    int ArrivalBookingCount,
    int ArrivalTicketCount,
    decimal ArrivalRevenue,
    decimal ArrivalRefundAmount,
    decimal ArrivalNetRevenue);

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

        var fromUtc = request.From.ToUniversalTime();
        var toUtc = request.To.ToUniversalTime();

        var query = _context.Set<Payment>()
            .AsNoTracking()
            .Include(p => p.Booking)
                .ThenInclude(b => b.FromStation)
            .Include(p => p.Booking)
                .ThenInclude(b => b.ToStation)
            .Include(p => p.Booking)
                .ThenInclude(b => b.Trip)
                    .ThenInclude(t => t!.Route)
            .Include(p => p.Booking)
                .ThenInclude(b => b.Tickets)
            .Where(p =>
                p.PaidAt.HasValue
                && p.PaidAt.Value >= fromUtc
                && p.PaidAt.Value < toUtc
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

        if (request.FromStationId.HasValue)
        {
            query = query.Where(p => p.Booking.FromStationId == request.FromStationId.Value);
        }

        if (request.ToStationId.HasValue)
        {
            query = query.Where(p => p.Booking.ToStationId == request.ToStationId.Value);
        }

        query = BookingReportQuerySupport.ApplyServiceTypeFilter(query, serviceType);

        var paymentData = await query
            .Select(p => new
            {
                p.Id,
                p.BookingId,
                p.PaidAt,
                p.Amount,
                p.RefundAmount,
                p.PaymentMethod,
                p.Booking.BookingType,
                p.Booking.SoldByStaffId,
                p.Booking.FromStationId,
                p.Booking.FromStation,
                p.Booking.ToStationId,
                p.Booking.ToStation,
                p.Booking.Trip,
                TicketCount = p.Booking.Tickets.Count(t => t.TicketStatus != TicketStatus.Cancelled)
            })
            .ToListAsync(cancellationToken);

        var rows = paymentData.Select(p => new RevenuePaymentRow(
            p.Id,
            p.BookingId,
            p.PaidAt!.Value,
            p.Amount,
            p.RefundAmount,
            p.PaymentMethod ?? string.Empty,
            p.BookingType ?? string.Empty,
            p.Trip?.Route?.RouteType ?? string.Empty,
            p.SoldByStaffId,
            p.TicketCount,
            p.FromStationId,
            p.FromStation?.StationName ?? string.Empty,
            p.FromStation?.StationCode ?? string.Empty,
            p.ToStationId,
            p.ToStation?.StationName ?? string.Empty,
            p.ToStation?.StationCode ?? string.Empty))
            .ToList();

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
            BuildStationBreakdown(rows),
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

    private static IReadOnlyList<StationRevenueDto> BuildStationBreakdown(IReadOnlyList<RevenuePaymentRow> rows)
    {
        // Build departure station breakdown
        var departureGroups = rows
            .Where(r => r.FromStationId.HasValue)
            .GroupBy(r => r.FromStationId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    StationName = g.First().FromStationName ?? "Unknown",
                    StationCode = g.First().FromStationCode ?? "N/A",
                    BookingIds = g.Select(r => r.BookingId).Distinct().ToList(),
                    TicketCount = g.Sum(r => r.TicketCount),
                    Revenue = g.Sum(r => r.Amount),
                    RefundAmount = g.Sum(r => r.RefundAmount)
                });

        // Build arrival station breakdown
        var arrivalGroups = rows
            .Where(r => r.ToStationId.HasValue)
            .GroupBy(r => r.ToStationId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    StationName = g.First().ToStationName ?? "Unknown",
                    StationCode = g.First().ToStationCode ?? "N/A",
                    BookingIds = g.Select(r => r.BookingId).Distinct().ToList(),
                    TicketCount = g.Sum(r => r.TicketCount),
                    Revenue = g.Sum(r => r.Amount),
                    RefundAmount = g.Sum(r => r.RefundAmount)
                });

        // Combine all station IDs (union of departure and arrival)
        var allStationIds = departureGroups.Keys
            .Union(arrivalGroups.Keys)
            .OrderBy(id => departureGroups.GetValueOrDefault(id)?.StationName ?? arrivalGroups.GetValueOrDefault(id)?.StationName ?? "")
            .ToList();

        var result = new List<StationRevenueDto>();
        foreach (var stationId in allStationIds)
        {
            var dep = departureGroups.GetValueOrDefault(stationId);
            var arr = arrivalGroups.GetValueOrDefault(stationId);

            result.Add(new StationRevenueDto(
                stationId,
                dep?.StationName ?? arr?.StationName ?? "Unknown",
                dep?.StationCode ?? arr?.StationCode ?? "N/A",
                dep?.BookingIds.Count ?? 0,
                dep?.TicketCount ?? 0,
                dep?.Revenue ?? 0m,
                dep?.RefundAmount ?? 0m,
                (dep?.Revenue ?? 0m) - (dep?.RefundAmount ?? 0m),
                arr?.BookingIds.Count ?? 0,
                arr?.TicketCount ?? 0,
                arr?.Revenue ?? 0m,
                arr?.RefundAmount ?? 0m,
                (arr?.Revenue ?? 0m) - (arr?.RefundAmount ?? 0m)));
        }

        return result;
    }
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
    string RouteType,
    Guid? SoldByStaffId,
    int TicketCount,
    Guid? FromStationId,
    string FromStationName,
    string FromStationCode,
    Guid? ToStationId,
    string ToStationName,
    string ToStationCode);

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
            "payos" or "online" or "qr" => PaymentSupport.PayOsProvider,
            "free" => PaymentSupport.FreePaymentMethod,
            _ => throw new ValidationException([new ValidationFailure("paymentMethod",
                "paymentMethod chỉ hỗ trợ Cash, PayOs hoặc Free.")])
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

public sealed record ExportBookingReportExcelQuery(
    string? Keyword = null,
    string? BookingStatus = null,
    string? PaymentStatus = null,
    string? ServiceType = null,
    string? PaymentMethod = null,
    Guid? SoldByStaffId = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    DateTimeOffset? DepartureFrom = null,
    DateTimeOffset? DepartureTo = null) : IRequest<byte[]>;

public sealed class ExportBookingReportExcelQueryHandler
    : IRequestHandler<ExportBookingReportExcelQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public ExportBookingReportExcelQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<byte[]> Handle(ExportBookingReportExcelQuery request, CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var query = BookingReportQuerySupport.BuildFilteredBookingQuery(
            _context,
            new GetBookingManagementListQuery(
                request.Keyword,
                request.BookingStatus,
                request.PaymentStatus,
                request.ServiceType,
                request.PaymentMethod,
                request.SoldByStaffId,
                request.CreatedFrom,
                request.CreatedTo,
                request.DepartureFrom,
                request.DepartureTo,
                1,
                int.MaxValue),
            actor);

        var rows = await BookingReportQuerySupport.ApplyDefaultOrdering(query)
            .Select(BookingReportQuerySupport.ManagementProjection)
            .ToListAsync(cancellationToken);

        var items = rows.Select(BookingReportQuerySupport.ToManagementItemDto).ToList();

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Bookings");

        var headers = new[]
        {
            "Mã Booking",
            "Ngày đặt",
            "Loại dịch vụ",
            "Tuyến",
            "Trạng thái Booking",
            "Trạng thái thanh toán",
            "Khách hàng",
            "Điện thoại",
            "Email",
            "Số hành khách",
            "Số vé",
            "Tổng tiền",
            "Giảm giá",
            "Thanh toán",
            "Còn lại",
            "Nhân viên bán",
            "Phương thức TT",
            "Ngày thanh toán",
            "Mã chuyến đi",
            "Mã chuyến về",
            "Giờ khởi hành",
            "Giờ đến"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
            worksheet.Cell(1, i + 1).Style.Font.Bold = true;
            worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        }

        for (var rowIndex = 0; rowIndex < items.Count; rowIndex++)
        {
            var item = items[rowIndex];
            var row = rowIndex + 2;

            worksheet.Cell(row, 1).Value = item.BookingCode;
            worksheet.Cell(row, 2).Value = item.BookedAt.ToOffset(VietnamOffset).DateTime.ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(row, 3).Value = item.ServiceType;
            worksheet.Cell(row, 4).Value = item.RouteName ?? "";
            worksheet.Cell(row, 5).Value = item.BookingStatus;
            worksheet.Cell(row, 6).Value = item.PaymentStatus;
            worksheet.Cell(row, 7).Value = item.CustomerName ?? item.ContactName;
            worksheet.Cell(row, 8).Value = item.ContactPhone ?? "";
            worksheet.Cell(row, 9).Value = item.ContactEmail ?? "";
            worksheet.Cell(row, 10).Value = item.PassengerCount;
            worksheet.Cell(row, 11).Value = item.TicketCount;
            worksheet.Cell(row, 12).Value = item.TotalAmount;
            worksheet.Cell(row, 13).Value = item.DiscountAmount;
            worksheet.Cell(row, 14).Value = item.PaidAmount;
            worksheet.Cell(row, 15).Value = item.RemainingAmount;
            worksheet.Cell(row, 16).Value = item.SoldByStaffName ?? "";
            worksheet.Cell(row, 17).Value = item.LatestPaymentMethod ?? "";
            worksheet.Cell(row, 18).Value = item.LatestPaidAt?.ToOffset(VietnamOffset).DateTime.ToString("dd/MM/yyyy HH:mm") ?? "";
            worksheet.Cell(row, 19).Value = item.TripCode ?? "";
            worksheet.Cell(row, 20).Value = item.ReturnTripCode ?? "";
            worksheet.Cell(row, 21).Value = item.DepartureAt?.ToOffset(VietnamOffset).DateTime.ToString("dd/MM/yyyy HH:mm") ?? "";
            worksheet.Cell(row, 22).Value = item.ArrivalAt?.ToOffset(VietnamOffset).DateTime.ToString("dd/MM/yyyy HH:mm") ?? "";
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
