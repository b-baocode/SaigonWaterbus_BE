using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Points;

public sealed record PointTransactionDto(
    Guid Id,
    string TransactionType,
    int Points,
    int BalanceAfter,
    string? Description,
    Guid? BookingId,
    string? BookingCode,
    DateTimeOffset CreatedAt);

public sealed record MyPointsDto(
    int PointBalance,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<PointTransactionDto> Transactions);

public sealed record GetMyPointsQuery(int Page = 1, int PageSize = 20) : IRequest<MyPointsDto>;

public sealed class GetMyPointsQueryValidator : AbstractValidator<GetMyPointsQuery>
{
    public GetMyPointsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetMyPointsQueryHandler : IRequestHandler<GetMyPointsQuery, MyPointsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetMyPointsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<MyPointsDto> Handle(GetMyPointsQuery request, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId
            ?? throw new ValidationException([new ValidationFailure("userId", "User must be authenticated.")]);

        var pointBalance = await _context.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.PointBalance)
            .SingleAsync(cancellationToken);

        var query = _context.Set<PointTransaction>().Where(t => t.UserId == userId);
        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new PointTransactionDto(
                t.Id,
                t.TransactionType,
                t.Points,
                t.BalanceAfter,
                t.Description,
                t.BookingId,
                t.Booking != null ? t.Booking.BookingCode : null,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        return new MyPointsDto(pointBalance, totalCount, request.Page, request.PageSize, transactions);
    }
}
