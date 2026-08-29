using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Reviews;

public sealed record AdminReviewDto(
    Guid ReviewId,
    int Rating,
    string? Comment,
    string Status,
    DateTimeOffset CreatedAt,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    Guid? BookingId,
    string? BookingCode,
    Guid? TripId,
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime);

public sealed record AdminReviewListDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AdminReviewDto> Items);

[Authorize(Roles = "Admin,Manager")]
public sealed record GetAdminReviewListQuery(
    string? Status = null,
    int? Rating = null,
    Guid? TripId = null,
    Guid? RouteId = null,
    int Page = 1,
    int PageSize = 20) : IRequest<AdminReviewListDto>;

public sealed class GetAdminReviewListQueryValidator : AbstractValidator<GetAdminReviewListQuery>
{
    public GetAdminReviewListQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => s is null || s is ReviewStatuses.Published or ReviewStatuses.Hidden)
            .WithMessage("Status phải là Published hoặc Hidden.");
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).When(x => x.Rating.HasValue);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetAdminReviewListQueryHandler : IRequestHandler<GetAdminReviewListQuery, AdminReviewListDto>
{
    private readonly IApplicationDbContext _context;

    public GetAdminReviewListQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AdminReviewListDto> Handle(
        GetAdminReviewListQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<Review>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(r => r.Status == request.Status);
        }

        if (request.Rating.HasValue)
        {
            query = query.Where(r => r.Rating == request.Rating.Value);
        }

        if (request.TripId.HasValue)
        {
            query = query.Where(r => r.TripId == request.TripId.Value);
        }

        if (request.RouteId.HasValue)
        {
            query = query.Where(r => r.Trip != null && r.Trip.RouteId == request.RouteId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.Created)
            .ThenBy(r => r.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new AdminReviewDto(
                r.Id,
                r.Rating,
                r.Comment,
                r.Status,
                r.Created,
                r.CustomerId,
                r.Customer.FullName,
                r.Customer.Email,
                r.BookingId,
                r.Booking != null ? r.Booking.BookingCode : null,
                r.TripId,
                r.Trip != null ? r.Trip.TripCode : null,
                r.Trip != null ? r.Trip.Route.RouteName : null,
                r.Trip != null ? r.Trip.DepartureTime : null))
            .ToListAsync(cancellationToken);

        return new AdminReviewListDto(totalCount, request.Page, request.PageSize, items);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record UpdateReviewStatusCommand(Guid ReviewId, string Status) : IRequest<AdminReviewDto>;

public sealed class UpdateReviewStatusCommandValidator : AbstractValidator<UpdateReviewStatusCommand>
{
    public UpdateReviewStatusCommandValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Status)
            .Must(s => s is ReviewStatuses.Published or ReviewStatuses.Hidden)
            .WithMessage("Status phải là Published hoặc Hidden.");
    }
}

public sealed class UpdateReviewStatusCommandHandler : IRequestHandler<UpdateReviewStatusCommand, AdminReviewDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateReviewStatusCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AdminReviewDto> Handle(
        UpdateReviewStatusCommand request,
        CancellationToken cancellationToken)
    {
        var review = await _context.Set<Review>()
            .Include(r => r.Customer)
            .Include(r => r.Booking)
            .Include(r => r.Trip)
                .ThenInclude(t => t!.Route)
            .SingleOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review not found.");

        if (review.Status != request.Status)
        {
            review.Status = request.Status;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new AdminReviewDto(
            review.Id,
            review.Rating,
            review.Comment,
            review.Status,
            review.Created,
            review.CustomerId,
            review.Customer.FullName,
            review.Customer.Email,
            review.BookingId,
            review.Booking?.BookingCode,
            review.TripId,
            review.Trip?.TripCode,
            review.Trip?.Route.RouteName,
            review.Trip?.DepartureTime);
    }
}

[Authorize(Roles = "Admin,Manager")]
public sealed record DeleteReviewCommand(Guid ReviewId) : IRequest;

public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
    }
}

public sealed class DeleteReviewCommandHandler : IRequestHandler<DeleteReviewCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteReviewCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(
        DeleteReviewCommand request,
        CancellationToken cancellationToken)
    {
        var review = await _context.Set<Review>()
            .SingleOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review not found.");

        _context.Set<Review>().Remove(review);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
