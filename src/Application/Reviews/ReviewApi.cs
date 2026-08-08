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
    Guid? TripId,
    string? TripCode,
    string? RouteName,
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
    MyTripReviewDto? MyReview,
    Guid BookingId,
    string BookingCode,
    bool IsRoundTrip);

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

    public static bool ContainsTrip(Booking booking, Guid tripId, Guid? tripSourceBookingId) =>
        booking.TripId == tripId
        || booking.ReturnTripId == tripId
        || booking.Passengers.Any(p => p.TripId == tripId)
        || booking.CharterBoats.Any(cb => cb.TripId == tripId)
        || (tripSourceBookingId.HasValue && booking.Id == tripSourceBookingId.Value);

    public static bool IsServiceCompleted(Booking booking)
    {
        var trips = ResolveAssociatedTrips(booking);
        return trips.Count > 0 && trips.All(t => t.TripStatus == TripStatus.Completed);
    }

    public static Trip? ResolveDisplayTrip(Booking booking) =>
        ResolveAssociatedTrips(booking)
            .OrderBy(t => t.DepartureTime)
            .ThenBy(t => t.TripCode)
            .FirstOrDefault();

    private static IReadOnlyList<Trip> ResolveAssociatedTrips(Booking booking)
    {
        var trips = new List<Trip>();
        var seenTripIds = new HashSet<Guid>();

        void AddTrip(Guid? tripId, Trip? trip)
        {
            if (!tripId.HasValue || trip is null || !seenTripIds.Add(tripId.Value))
            {
                return;
            }

            trips.Add(trip);
        }

        AddTrip(booking.TripId, booking.Trip);
        AddTrip(booking.ReturnTripId, booking.ReturnTrip);
        foreach (var passenger in booking.Passengers)
        {
            AddTrip(passenger.TripId, passenger.Trip);
        }

        foreach (var charterBoat in booking.CharterBoats)
        {
            AddTrip(charterBoat.TripId, charterBoat.Trip);
        }

        return trips;
    }
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

        var candidateBookings = await ReviewSupport
            .EligibleBookingsForTrip(_context, userId, trip.Id, trip.SourceBookingId)
            .Include(b => b.Trip!)
            .Include(b => b.ReturnTrip!)
            .Include(b => b.Passengers)
                .ThenInclude(p => p.Trip!)
            .Include(b => b.CharterBoats)
                .ThenInclude(cb => cb.Trip!)
            .OrderBy(b => b.Created)
            .ToListAsync(cancellationToken);
        if (candidateBookings.Count == 0)
        {
            throw new ValidationException(
                [new ValidationFailure("tripId", "Bạn không có vé trên chuyến này nên không thể đánh giá.")]);
        }

        var booking = candidateBookings.FirstOrDefault(ReviewSupport.IsServiceCompleted);
        if (booking is null)
        {
            throw new ValidationException(
                [new ValidationFailure("tripId", "Booking chưa hoàn tất toàn bộ chuyến nên chưa thể đánh giá.")]);
        }

        var alreadyReviewed = await _context.Set<Review>()
            .AnyAsync(r => r.CustomerId == userId && r.BookingId == booking.Id, cancellationToken);
        if (alreadyReviewed)
        {
            throw new ValidationException(
                [new ValidationFailure("tripId", "Bạn đã đánh giá booking này rồi.")]);
        }

        var review = new Review
        {
            CustomerId = userId,
            BookingId = booking.Id,
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

        var bookings = await _context.Set<Booking>()
            .AsNoTracking()
            .Include(b => b.Trip!)
                .ThenInclude(t => t.Route)
            .Include(b => b.ReturnTrip!)
                .ThenInclude(t => t.Route)
            .Include(b => b.Passengers)
                .ThenInclude(p => p.Trip!)
                    .ThenInclude(t => t.Route)
            .Include(b => b.CharterBoats)
                .ThenInclude(cb => cb.Trip!)
                    .ThenInclude(t => t.Route)
            .Where(b => b.UserId == userId
                && (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Completed)
                && (b.TripId != null
                    || b.ReturnTripId != null
                    || b.Passengers.Any(p => p.TripId != null)
                    || b.CharterBoats.Any(cb => cb.TripId != null)))
            .ToListAsync(cancellationToken);

        var reviewableBookings = bookings
            .Where(ReviewSupport.IsServiceCompleted)
            .Select(b => new { Booking = b, DisplayTrip = ReviewSupport.ResolveDisplayTrip(b) })
            .Where(x => x.DisplayTrip is not null)
            .OrderByDescending(x => x.DisplayTrip!.DepartureTime)
            .ThenBy(x => x.Booking.BookingCode)
            .ToList();

        var totalCount = reviewableBookings.Count;
        var pageBookings = reviewableBookings
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var bookingIds = pageBookings.Select(x => x.Booking.Id).ToList();
        var reviews = await _context.Set<Review>()
            .AsNoTracking()
            .Where(r => r.CustomerId == userId
                && r.BookingId.HasValue
                && bookingIds.Contains(r.BookingId.Value))
            .ToListAsync(cancellationToken);
        var reviewsByBookingId = reviews
            .GroupBy(r => r.BookingId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Created).First());

        var items = pageBookings
            .Select(x =>
            {
                var trip = x.DisplayTrip!;
                reviewsByBookingId.TryGetValue(x.Booking.Id, out var review);
                return new ReviewableTripDto(
                    trip.Id,
                    trip.TripCode,
                    trip.Route.RouteName,
                    trip.DepartureTime,
                    trip.ArrivalTime,
                    review is null ? null : ReviewSupport.ToMyDto(review),
                    x.Booking.Id,
                    x.Booking.BookingCode,
                    x.Booking.ReturnTripId.HasValue);
            })
            .ToList();

        return new ReviewableTripListDto(totalCount, request.Page, request.PageSize, items);
    }
}

public sealed record GetPublishedReviewsQuery(int Page = 1, int PageSize = 20) : IRequest<TripReviewListDto>;

public sealed class GetPublishedReviewsQueryValidator : AbstractValidator<GetPublishedReviewsQuery>
{
    public GetPublishedReviewsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetPublishedReviewsQueryHandler : IRequestHandler<GetPublishedReviewsQuery, TripReviewListDto>
{
    private readonly IApplicationDbContext _context;

    public GetPublishedReviewsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TripReviewListDto> Handle(
        GetPublishedReviewsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<Review>()
            .AsNoTracking()
            .Where(r => r.Status == ReviewStatuses.Published);

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
                r.TripId,
                r.Trip != null ? r.Trip.TripCode : null,
                r.Trip != null ? r.Trip.Route.RouteName : null,
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
