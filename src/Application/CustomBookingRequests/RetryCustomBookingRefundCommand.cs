using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ForbiddenAccessException = SaigonWaterbus.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public sealed record RetryCustomBookingRefundCommand(
    Guid Id,
    string RefundBankBin,
    string RefundAccountNumber,
    string RefundAccountName)
    : IRequest<CustomBookingRequestDto>;

public sealed class RetryCustomBookingRefundCommandValidator
    : AbstractValidator<RetryCustomBookingRefundCommand>
{
    public RetryCustomBookingRefundCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id yêu cầu thuê tàu không hợp lệ.");
        RuleFor(x => x.RefundBankBin)
            .NotEmpty()
            .WithMessage("Mã BIN ngân hàng nhận hoàn tiền là bắt buộc.")
            .Must(CustomBookingRefundAccountValidation.IsValidBankBin)
            .WithMessage("Mã BIN ngân hàng nhận hoàn tiền phải gồm đúng 6 chữ số theo chuẩn PayOS.");
        RuleFor(x => x.RefundAccountNumber)
            .NotEmpty()
            .WithMessage("Số tài khoản nhận hoàn tiền là bắt buộc.")
            .Must(CustomBookingRefundAccountValidation.IsValidAccountNumber)
            .WithMessage("Số tài khoản nhận hoàn tiền chỉ được gồm chữ số và không vượt quá 50 ký tự.");
        RuleFor(x => x.RefundAccountName)
            .NotEmpty()
            .WithMessage("Tên tài khoản nhận hoàn tiền là bắt buộc.")
            .MaximumLength(CustomBookingRefundAccountValidation.MaxAccountNameLength)
            .WithMessage("Tên tài khoản nhận hoàn tiền không được vượt quá 150 ký tự.");
    }
}

public sealed class RetryCustomBookingRefundCommandHandler
    : IRequestHandler<RetryCustomBookingRefundCommand, CustomBookingRequestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomBookingPaymentGateway _paymentGateway;

    public RetryCustomBookingRefundCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider,
        ICustomBookingPaymentGateway paymentGateway)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
        _paymentGateway = paymentGateway;
    }

    public async Task<CustomBookingRequestDto> Handle(
        RetryCustomBookingRefundCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        var customRequest = await CustomBookingRequestSupport.IncludeDetails(_context.Set<CustomBookingRequest>())
            .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy yêu cầu thuê tàu.");

        var canManage = AuthSupport.IsAdmin(actor);
        var isOwner = AuthSupport.IsCustomer(actor) && customRequest.UserId == actor.Id;
        if (!canManage && !isOwner)
        {
            throw new ForbiddenAccessException();
        }

        if (customRequest.Status is not (CustomBookingRequestStatus.Cancelled or CustomBookingRequestStatus.Confirmed))
        {
            throw AuthSupport.CreateValidationException(
                nameof(customRequest.Status),
                "Chỉ retry hoàn tiền cho booking đã hủy hoặc đang chờ xử lý hủy do hoàn tiền lỗi.");
        }

        var quote = customRequest.Quote
            ?? throw AuthSupport.CreateValidationException("quote", "Booking chưa có báo giá để hoàn tiền.");
        if (quote.RefundAmount <= 0)
        {
            throw AuthSupport.CreateValidationException("refundAmount", "Booking không có số tiền đủ điều kiện hoàn.");
        }

        if (!string.Equals(quote.RefundStatus, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                nameof(quote.RefundStatus),
                "Chỉ retry khi lệnh hoàn tiền đang ở trạng thái Failed.");
        }

        var now = _timeProvider.GetUtcNow();
        var referenceId = CreateRefundReferenceId(customRequest, now);
        quote.RefundBankBin = CustomBookingRefundAccountValidation.NormalizeRequiredBankBin(
            request.RefundBankBin,
            nameof(request.RefundBankBin));
        quote.RefundAccountNumber = CustomBookingRefundAccountValidation.NormalizeRequiredAccountNumber(
            request.RefundAccountNumber,
            nameof(request.RefundAccountNumber));
        quote.RefundAccountName = CustomBookingRefundAccountValidation.NormalizeRequiredAccountName(
            request.RefundAccountName,
            nameof(request.RefundAccountName));
        quote.RefundReferenceId = referenceId;
        quote.RefundStatus = "Pending";
        quote.RefundFailureReason = null;
        quote.RefundRequestedAt = now;
        quote.RefundProcessedAt = null;
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await _paymentGateway.CreateRefundPayoutAsync(
                new CustomBookingRefundPayoutRequest(
                    referenceId,
                    ToPayOsAmount(quote.RefundAmount),
                    CreateRefundDescription(customRequest),
                    quote.RefundBankBin,
                    quote.RefundAccountNumber,
                    quote.RefundAccountName,
                    Guid.NewGuid().ToString("D")),
                cancellationToken);

            quote.RefundPayoutId = result.PayoutId;
            quote.RefundStatus = result.Status;
            quote.RefundFailureReason = null;
            quote.RefundProcessedAt = _timeProvider.GetUtcNow();
            if (!CustomBookingRefundPayoutStatus.IsAccepted(quote))
            {
                quote.RefundFailureReason = CustomBookingRefundPayoutStatus.CreateNotAcceptedReason(
                    result.Status,
                    result.Description);
                await _context.SaveChangesAsync(cancellationToken);
                throw AuthSupport.CreateValidationException("refund", quote.RefundFailureReason);
            }

            if (customRequest.Status != CustomBookingRequestStatus.Cancelled)
            {
                customRequest.Status = CustomBookingRequestStatus.Cancelled;
                customRequest.CancelledAt = _timeProvider.GetUtcNow();
                customRequest.CancelledByUserId = actor.Id;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            quote.RefundStatus = "Failed";
            quote.RefundFailureReason = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            throw AuthSupport.CreateValidationException("refund", ex.Message);
        }

        var routeSegments = await CustomBookingRequestSupport.GetMatchingRouteSegmentsAsync(
            _context,
            customRequest,
            cancellationToken);
        return CustomBookingRequestDto.From(customRequest, routeSegments);
    }

    private static long ToPayOsAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Truncate(amount) != amount || amount > long.MaxValue)
        {
            throw AuthSupport.CreateValidationException("refundAmount", "Số tiền hoàn phải là số nguyên VND lớn hơn 0.");
        }

        return (long)amount;
    }

    private static string CreateRefundReferenceId(CustomBookingRequest request, DateTimeOffset now) =>
        $"CBR-{request.Id.ToString("N")[..24].ToUpperInvariant()}R{now.ToUnixTimeSeconds() % 10000000:D7}";

    private static string CreateRefundDescription(CustomBookingRequest request) =>
        $"Hoan tien SWB {request.Id.ToString("N")[..8].ToUpperInvariant()}";
}
