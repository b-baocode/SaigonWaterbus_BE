using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Promotions;

public class PromotionEligibilitySupportTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task MaxOnePerAccountRejectsActivePreviousUse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.MaxUsesPerAccount = 1);
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.PendingPayment));
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            PromotionEligibilitySupport.EnsureAndCalculateAsync(
                context, promotion, userContext.UserId.Value, 100_000m, Now, "promotionCode"));

        exception.Errors["promotionCode"]
            .ShouldContain("Khuyến mãi này mỗi tài khoản chỉ được sử dụng 1 lần.");
    }

    [Test]
    public async Task MaxOnePerAccountIgnoresCancelledPreviousUse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.MaxUsesPerAccount = 1);
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.Cancelled));
        await context.SaveChangesAsync();

        var discount = await PromotionEligibilitySupport.EnsureAndCalculateAsync(
            context, promotion, userContext.UserId.Value, 100_000m, Now, "promotionCode");

        discount.ShouldBe(10_000m);
    }

    [Test]
    public async Task NoPerAccountLimitAllowsPreviousUse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion();
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.PendingPayment));
        await context.SaveChangesAsync();

        await PromotionEligibilitySupport.EnsureAndCalculateAsync(
            context, promotion, userContext.UserId.Value, 100_000m, Now, "promotionCode");
    }

    [Test]
    public async Task DiscountAppliesMaxCapAndVndRounding()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var promotion = Promotion(p =>
        {
            p.DiscountValue = 10;
            p.MaxDiscountAmount = 5_000m;
        });
        context.Set<Promotion>().Add(promotion);
        await context.SaveChangesAsync();

        // 10% của 123.457 = 12.345,7 → bị chặn ở trần 5.000.
        var discount = await PromotionEligibilitySupport.EnsureAndCalculateAsync(
            context, promotion, null, 123_457m, Now, "promotionCode");

        discount.ShouldBe(5_000m);
    }

    [Test]
    public async Task UsageLimitExhaustedIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.UsageLimit = 1);
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.Confirmed));
        await context.SaveChangesAsync();

        var result = await PromotionEligibilitySupport.EvaluateAsync(
            context, promotion, null, 100_000m, Now);

        result.IsValid.ShouldBeFalse();
        result.Reason.ShouldBe("Khuyến mãi đã hết lượt sử dụng.");
    }

    [Test]
    public async Task BudgetCapExhaustedIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.BudgetCap = 15_000m);
        context.Set<Promotion>().Add(promotion);
        var used = Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.Confirmed);
        used.DiscountAmount = 10_000m;
        context.Set<Booking>().Add(used);
        await context.SaveChangesAsync();

        // Đã tiêu 10.000; đơn mới giảm 10% của 100.000 = 10.000 → 20.000 > cap 15.000.
        var result = await PromotionEligibilitySupport.EvaluateAsync(
            context, promotion, null, 100_000m, Now);

        result.IsValid.ShouldBeFalse();
        result.Reason.ShouldBe("Khuyến mãi đã hết ngân sách.");
    }

    [Test]
    public async Task FirstBookingOnlyRejectsReturningCustomer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.FirstBookingOnly = true);
        context.Set<Promotion>().Add(promotion);
        // Booking cũ không dùng mã này, nhưng vẫn tính là khách đã từng đặt.
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotionId: null, BookingStatus.Completed));
        await context.SaveChangesAsync();

        var result = await PromotionEligibilitySupport.EvaluateAsync(
            context, promotion, userContext.UserId.Value, 100_000m, Now);

        result.IsValid.ShouldBeFalse();
        result.Reason.ShouldBe("Khuyến mãi chỉ dành cho lần đặt đầu tiên.");
    }

    [Test]
    public async Task PublicPromotionListHidesCodeWhenCurrentUserReachedAccountLimit()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var usedPromotion = Promotion(p => p.MaxUsesPerAccount = 1);
        usedPromotion.PromotionCode = "USEDONCE";
        var availablePromotion = Promotion(p => p.MaxUsesPerAccount = 1);
        availablePromotion.PromotionCode = "AVAILABLE";
        context.Set<Promotion>().AddRange(usedPromotion, availablePromotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, usedPromotion.Id, BookingStatus.Completed));
        await context.SaveChangesAsync();

        var result = await new GetPublicPromotionListQueryHandler(context, userContext, new FixedTimeProvider(Now))
            .Handle(new GetPublicPromotionListQuery(), CancellationToken.None);

        result.Select(x => x.PromotionCode).ShouldBe(["AVAILABLE"]);
    }

    [Test]
    public async Task PublicPromotionListShowsCodeAgainWhenPreviousUseWasReleased()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.MaxUsesPerAccount = 1);
        promotion.PromotionCode = "WELCOME10";
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.Cancelled));
        await context.SaveChangesAsync();

        var result = await new GetPublicPromotionListQueryHandler(context, userContext, new FixedTimeProvider(Now))
            .Handle(new GetPublicPromotionListQuery(), CancellationToken.None);

        result.Single().PromotionCode.ShouldBe("WELCOME10");
    }

    [Test]
    public async Task AdminPromotionListReturnsRemainingBudgetAndIgnoresReleasedBookings()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(p => p.BudgetCap = 50_000m);
        context.Set<Promotion>().Add(promotion);

        var activeBooking = Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.Confirmed);
        activeBooking.DiscountAmount = 10_000m;
        var cancelledBooking = Booking(userContext.UserId.Value, promotion.Id, BookingStatus.Cancelled);
        cancelledBooking.DiscountAmount = 20_000m;
        var expiredBooking = Booking(userContext.UserId.Value, promotion.Id, BookingStatus.Expired);
        expiredBooking.DiscountAmount = 30_000m;
        context.Set<Booking>().AddRange(activeBooking, cancelledBooking, expiredBooking);
        await context.SaveChangesAsync();

        var result = await new GetPromotionListQueryHandler(context, new FixedTimeProvider(Now))
            .Handle(new GetPromotionListQuery(), CancellationToken.None);

        var item = result.Single(x => x.PromotionId == promotion.Id);
        item.TotalUsed.ShouldBe(1);
        item.BudgetSpent.ShouldBe(10_000m);
        item.RemainingBudget.ShouldBe(40_000m);
    }

    [Test]
    public async Task AdminPromotionListReturnsNullRemainingBudgetWhenBudgetIsUnlimited()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var promotion = Promotion();
        context.Set<Promotion>().Add(promotion);
        await context.SaveChangesAsync();

        var result = await new GetPromotionListQueryHandler(context, new FixedTimeProvider(Now))
            .Handle(new GetPromotionListQuery(), CancellationToken.None);

        result.Single(x => x.PromotionId == promotion.Id).RemainingBudget.ShouldBeNull();
    }

    private static Promotion Promotion(Action<Promotion>? configure = null)
    {
        var promotion = new Promotion
        {
            PromotionCode = $"PROMO{Guid.NewGuid():N}"[..20],
            PromotionName = "Test promotion",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(2030, 12, 31, 0, 0, 0, TimeSpan.Zero),
            Status = PromotionStatus.Active
        };
        configure?.Invoke(promotion);
        return promotion;
    }

    private static Booking Booking(Guid userId, Guid? promotionId, BookingStatus status) =>
        new()
        {
            UserId = userId,
            PromotionId = promotionId,
            BookingCode = $"BK{Guid.NewGuid():N}"[..20],
            ContactName = "Test User",
            BookingStatus = status
        };
}
