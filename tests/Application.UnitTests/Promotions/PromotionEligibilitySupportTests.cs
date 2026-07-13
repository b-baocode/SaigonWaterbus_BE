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
