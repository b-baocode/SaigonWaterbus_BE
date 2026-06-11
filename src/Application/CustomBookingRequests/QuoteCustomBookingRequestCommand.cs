using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record QuoteCustomBookingRequestCommand(
    Guid Id,
    decimal QuotedPrice,
    string? Currency,
    string? PriceNote,
    DateTimeOffset? ValidUntil) : IRequest<CustomBookingRequestDto>;

public sealed class QuoteCustomBookingRequestCommandValidator : AbstractValidator<QuoteCustomBookingRequestCommand>
{
    public QuoteCustomBookingRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.QuotedPrice).GreaterThan(0).PrecisionScale(12, 2, false);
        RuleFor(x => x.Currency)
            .Must(CustomBookingRequestSupport.IsValidCurrencyCode)
            .WithMessage("Currency phải là mã ISO 4217 gồm 3 chữ cái, ví dụ VND.")
            .When(x => x.Currency is not null);
        RuleFor(x => x.PriceNote).MaximumLength(1000).When(x => x.PriceNote is not null);
    }
}

public sealed class QuoteCustomBookingRequestCommandHandler
    : IRequestHandler<QuoteCustomBookingRequestCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public QuoteCustomBookingRequestCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomBookingRequestDto> Handle(
        QuoteCustomBookingRequestCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await CustomBookingRequestSupport.EnsureCurrentUserCanManageCustomBookingRequestsAsync(
            _context,
            _userContext,
            cancellationToken);

        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        CustomBookingRequestSupport.EnsureCanQuote(customRequest);
        var now = _timeProvider.GetUtcNow();
        var validUntil = CustomBookingRequestSupport.NormalizeUtc(request.ValidUntil);
        CustomBookingRequestSupport.EnsureQuoteIsValid(validUntil, now);

        var depositAmount = decimal.Round(request.QuotedPrice * 0.5m, 0, MidpointRounding.AwayFromZero);
        var remainingAmount = request.QuotedPrice - depositAmount;

        if (customRequest.Quote is null)
        {
            customRequest.Quote = new CustomBookingQuote
            {
                CustomBookingRequestId = customRequest.Id
            };
            _context.Set<CustomBookingQuote>().Add(customRequest.Quote);
        }

        customRequest.Quote.QuotedPrice = request.QuotedPrice;
        customRequest.Quote.DepositAmount = depositAmount;
        customRequest.Quote.RemainingAmount = remainingAmount;
        customRequest.Quote.Currency = CustomBookingRequestSupport.NormalizeCurrency(request.Currency);
        customRequest.Quote.PriceNote = string.IsNullOrWhiteSpace(request.PriceNote) ? null : request.PriceNote.Trim();
        customRequest.Quote.ValidUntil = validUntil;
        customRequest.Status = CustomBookingRequestStatus.Quoted;
        customRequest.QuotedAt = now;
        customRequest.QuotedByUserId = actor.Id;
        customRequest.QuoteAcceptedAt = null;

        await _context.SaveChangesAsync(cancellationToken);

        customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleAsync(x => x.Id == customRequest.Id, cancellationToken);

        return CustomBookingRequestDto.From(customRequest);
    }
}
