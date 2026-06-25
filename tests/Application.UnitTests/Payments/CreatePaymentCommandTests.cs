using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Payments;

public class CreatePaymentCommandTests
{
    [Test]
    public async Task CustomBookingDepositCreatesNewPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new CustomBooking
        {
            UserId = userId,
            BookingCode = "CB-PAYMENT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = VesselRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(),
            TimeProvider.System);

        var result = await handler.Handle(
            new CreatePaymentCommand(booking.Id, BookingPaymentOption.Deposit),
            CancellationToken.None);

        result.Amount.ShouldBe(5000);
        result.PaymentStatus.ShouldBe("Pending");
        result.CheckoutUrl.ShouldBe("https://example.test/checkout");
        context.Set<Payment>().Count().ShouldBe(1);
        context.Set<Payment>().Single().PaymentPurpose.ShouldBe("Deposit");
    }

    [Test]
    public async Task CustomBookingPaymentGatewayFailureRestoresUnpaidAmounts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new CustomBooking
        {
            UserId = userId,
            BookingCode = "CB-PAYMENT-FAIL",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = VesselRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var gatewayFailure = new PaymentGatewayException("PayOS failed");
        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new TestPaymentGateway(gatewayFailure, gatewayFailure),
            TimeProvider.System);

        await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreatePaymentCommand(booking.Id, BookingPaymentOption.Full),
                CancellationToken.None));

        booking.PaymentStatus.ShouldBe("Unpaid");
        booking.DepositAmount.ShouldBe(0);
        booking.RemainingAmount.ShouldBe(10000);
        context.Set<Payment>().Count().ShouldBe(1);
        context.Set<Payment>().Single().PaymentStatus.ShouldBe("Failed");
    }

    private sealed class TestPaymentGateway(
        PaymentGatewayException? createPaymentException = null,
        PaymentGatewayException? getPaymentException = null)
        : ICustomBookingPaymentGateway
    {
        public Task<CustomBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CustomBookingDepositPaymentRequest request,
            CancellationToken cancellationToken)
        {
            if (createPaymentException is not null)
            {
                throw createPaymentException;
            }

            return Task.FromResult(new CustomBookingDepositPaymentResult(
                "payment-link-id",
                "https://example.test/checkout",
                "qr",
                "PENDING"));
        }

        public Task<CustomBookingPaymentStatusResult> GetPaymentAsync(
            long orderCode,
            CancellationToken cancellationToken)
        {
            if (getPaymentException is not null)
            {
                throw getPaymentException;
            }

            return Task.FromResult(new CustomBookingPaymentStatusResult(
                orderCode,
                null,
                "PENDING",
                "payment-link-id",
                "https://example.test/checkout"));
        }

        public Task<CustomBookingPaymentCancellationResult> CancelPaymentAsync(
            long orderCode,
            string reason,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomBookingPaymentCancellationResult(
                "payment-link-id",
                "CANCELLED",
                reason));

        public Task<CustomBookingRefundPayoutResult> CreateRefundPayoutAsync(
            CustomBookingRefundPayoutRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomBookingRefundPayoutResult(
                "payout-id",
                request.ReferenceId,
                "PENDING",
                null));

        public Task<CustomBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
            string referenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CustomBookingRefundPayoutResult?>(null);

        public bool IsValidWebhook(CustomBookingDepositPaymentWebhook webhook) => true;
    }
}
