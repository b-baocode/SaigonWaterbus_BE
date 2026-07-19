using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Reviews;

public static class ReviewStatuses
{
    public const string Published = "Published";
    public const string Hidden = "Hidden";
}

public sealed record TripReviewDto(
    Guid ReviewId,
    Guid TripId,
    string? TripCode,
    int Rating,
    string? Comment,
    string CustomerName,
    DateTimeOffset CreatedAt);

public sealed record TripReviewListDto(
    double? AverageRating,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<TripReviewDto> Items);

public sealed record MyTripReviewDto(
    Guid ReviewId,
    Guid TripId,
    int Rating,
    string? Comment,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record ReviewableTripDto(
    Guid TripId,
    string TripCode,
    string RouteName,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    MyTripReviewDto? MyReview);

public sealed record ReviewableTripListDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<ReviewableTripDto> Items);

internal static class ReviewSupport
{
    public static Guid GetRequiredUserId(IUserContext userContext) =>
        userContext.UserId
        ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

    /// <summary>
    /// Booking cho phép user đánh giá trip: booking của chính user, còn hiệu lực
    /// (Confirmed/Completed), gắn với trip qua chiều đi, chiều về, hành khách per-leg
    /// (round-trip) hoặc là charter booking sinh ra trip.
    /// </summary>
    public static IQueryable<Booking> EligibleBookingsForTrip(
        IApplicationDbContext context,
        Guid userId,
        Guid tripId,
        Guid? tripSourceBookingId) =>
        context.Set<Booking>()
            .Where(b => b.UserId == userId
                && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed)
                && (b.TripId == tripId
                    || b.ReturnTripId == tripId
                    || b.Passengers.Any(p => p.TripId == tripId)
                    || (tripSourceBookingId != null && b.Id == tripSourceBookingId)));

    public static MyTripReviewDto ToMyDto(Review review) =>
        new(review.Id, review.TripId!.Value, review.Rating, review.Comment, review.Status, review.Created);
}

public sealed record CreateTripReviewCommand(
    Guid TripId,
    int Rating,
    string? Comment) : IRequest<MyTripReviewDto>;

public sealed class CreateTripReviewCommandValidator : AbstractValidator<CreateTripReviewCommand>
{
    public CreateTripReviewCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public sealed class CreateTripReviewCommandHandler : IRequestHandler<CreateTripReviewCommand, MyTripReviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public CreateTripReviewCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<MyTripReviewDto> Handle(CreateTripReviewCommand request, CancellationToken cancellationToken)
    {
        var userId = ReviewSupport.GetRequiredUserId(_userContext);

        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(t => t.Id == request.TripId)
            .Select(t => new { t.Id, t.TripStatus, t.SourceBookingId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        if (trip.TripStatus != TripStatus.Completed)
        {
            throw new ValidationException(
                [new ValidationFailure("tripId", "Chỉ có thể đánh giá chuyến đã hoàn thành.")]);
        }

        var bookingId = await ReviewSupport
            .EligibleBookingsForTrip(_context, userId, trip.Id, trip.SourceBookingId)
            .OrderBy(b => b.Created)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(
                [new ValidationFailure("tripId", "Bạn không có vé trên chuyến này nên không thể đánh giá.")]);

        var alreadyReviewed = await _context.Set<Review>()
            .AnyAsync(r => r.CustomerId == userId && r.TripId == trip.Id, cancellationToken);
        if (alreadyReviewed)
        {
            throw new ValidationException(
                [new ValidationFailure("tripId", "Bạn đã đánh giá chuyến này rồi.")]);
        }

        var review = new Review
        {
            CustomerId = userId,
            BookingId = bookingId,
            TripId = trip.Id,
            Rating = request.Rating,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            Status = ReviewStatuses.Hidden
        };
        _context.Set<Review>().Add(review);
        await _context.SaveChangesAsync(cancellationToken);

        return ReviewSupport.ToMyDto(review);
    }
}

public sealed record GetMyReviewableTripsQuery(int Page = 1, int PageSize = 20) : IRequest<ReviewableTripListDto>;

public sealed class GetMyReviewableTripsQueryValidator : AbstractValidator<GetMyReviewableTripsQuery>
{
    public GetMyReviewableTripsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetMyReviewableTripsQueryHandler
    : IRequestHandler<GetMyReviewableTripsQuery, ReviewableTripListDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyReviewableTripsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<ReviewableTripListDto> Handle(
        GetMyReviewableTripsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = ReviewSupport.GetRequiredUserId(_userContext);

        var query = _context.Set<Trip>()
            .AsNoTracking()
            .Where(t => t.TripStatus == TripStatus.Completed)
            .Where(t => _context.Set<Booking>().Any(b => b.UserId == userId
                && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed)
                && (b.TripId == t.Id
                    || b.ReturnTripId == t.Id
                    || b.Passengers.Any(p => p.TripId == t.Id)
                    || (t.SourceBookingId != null && b.Id == t.SourceBookingId))));

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.DepartureTime)
            .ThenBy(t => t.TripCode)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new ReviewableTripDto(
                t.Id,
                t.TripCode,
                t.Route.RouteName,
                t.DepartureTime,
                t.ArrivalTime,
                _context.Set<Review>()
                    .Where(r => r.CustomerId == userId && r.TripId == t.Id)
                    .Select(r => new MyTripReviewDto(
                        r.Id, t.Id, r.Rating, r.Comment, r.Status, r.Created))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new ReviewableTripListDto(totalCount, request.Page, request.PageSize, items);
    }
}

public sealed record GetTripReviewsQuery(Guid TripId, int Page = 1, int PageSize = 20) : IRequest<TripReviewListDto>;

public sealed class GetTripReviewsQueryValidator : AbstractValidator<GetTripReviewsQuery>
{
    public GetTripReviewsQueryValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetTripReviewsQueryHandler : IRequestHandler<GetTripReviewsQuery, TripReviewListDto>
{
    private readonly IApplicationDbContext _context;

    public GetTripReviewsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TripReviewListDto> Handle(GetTripReviewsQuery request, CancellationToken cancellationToken)
    {
        var tripExists = await _context.Set<Trip>()
            .AnyAsync(t => t.Id == request.TripId, cancellationToken);
        if (!tripExists)
        {
            throw new NotFoundException("Trip not found.");
        }

        var query = _context.Set<Review>()
            .AsNoTracking()
            .Where(r => r.TripId == request.TripId && r.Status == ReviewStatuses.Published);

        return await ReviewListSupport.ToPagedListAsync(query, request.Page, request.PageSize, cancellationToken);
    }
}

public sealed record GetRouteReviewsQuery(Guid RouteId, int Page = 1, int PageSize = 20) : IRequest<TripReviewListDto>;

public sealed class GetRouteReviewsQueryValidator : AbstractValidator<GetRouteReviewsQuery>
{
    public GetRouteReviewsQueryValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetRouteReviewsQueryHandler : IRequestHandler<GetRouteReviewsQuery, TripReviewListDto>
{
    private readonly IApplicationDbContext _context;

    public GetRouteReviewsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TripReviewListDto> Handle(GetRouteReviewsQuery request, CancellationToken cancellationToken)
    {
        var routeExists = await _context.Set<Route>()
            .AnyAsync(r => r.Id == request.RouteId, cancellationToken);
        if (!routeExists)
        {
            throw new NotFoundException("Route not found.");
        }

        var query = _context.Set<Review>()
            .AsNoTracking()
            .Where(r => r.Trip != null
                && r.Trip.RouteId == request.RouteId
                && r.Status == ReviewStatuses.Published);

        return await ReviewListSupport.ToPagedListAsync(query, request.Page, request.PageSize, cancellationToken);
    }
}

internal static class ReviewListSupport
{
    public static async Task<TripReviewListDto> ToPagedListAsync(
        IQueryable<Review> publishedReviews,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await publishedReviews.CountAsync(cancellationToken);
        var averageRating = await publishedReviews
            .Select(r => (double?)r.Rating)
            .AverageAsync(cancellationToken);

        var items = await publishedReviews
            .OrderByDescending(r => r.Created)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new TripReviewDto(
                r.Id,
                r.TripId!.Value,
                r.Trip != null ? r.Trip.TripCode : null,
                r.Rating,
                r.Comment,
                r.Customer.FullName,
                r.Created))
            .ToListAsync(cancellationToken);

        return new TripReviewListDto(
            averageRating.HasValue ? Math.Round(averageRating.Value, 1) : null,
            totalCount,
            page,
            pageSize,
            items);
    }
}
