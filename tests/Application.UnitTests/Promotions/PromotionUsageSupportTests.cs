using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Promotions;

public class PromotionUsageSupportTests
{
    [Test]
    public async Task OncePerAccountRejectsActivePreviousUse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(PromotionAccountUsagePolicy.OncePerAccount);
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.PendingPayment));
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            PromotionUsageSupport.EnsureAccountCanUsePromotionAsync(
                context,
                promotion,
                userContext.UserId.Value,
                "promotionCode",
                CancellationToken.None));

        exception.Errors["promotionCode"]
            .ShouldContain("Promotion này mỗi tài khoản chỉ được sử dụng 1 lần.");
    }

    [Test]
    public async Task OncePerAccountIgnoresCancelledPreviousUse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(PromotionAccountUsagePolicy.OncePerAccount);
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.Cancelled));
        await context.SaveChangesAsync();

        await PromotionUsageSupport.EnsureAccountCanUsePromotionAsync(
            context,
            promotion,
            userContext.UserId.Value,
            "promotionCode",
            CancellationToken.None);
    }

    [Test]
    public async Task MultiplePerAccountAllowsPreviousUse()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var promotion = Promotion(PromotionAccountUsagePolicy.MultiplePerAccount);
        context.Set<Promotion>().Add(promotion);
        context.Set<Booking>().Add(Booking(userContext.UserId!.Value, promotion.Id, BookingStatus.PendingPayment));
        await context.SaveChangesAsync();

        await PromotionUsageSupport.EnsureAccountCanUsePromotionAsync(
            context,
            promotion,
            userContext.UserId.Value,
            "promotionCode",
            CancellationToken.None);
    }

    [Test]
    public void DecrementUsageDoesNotGoBelowZero()
    {
        var promotion = Promotion(PromotionAccountUsagePolicy.MultiplePerAccount);

        PromotionUsageSupport.DecrementUsage(promotion);

        promotion.UsageCount.ShouldBe(0);
    }

    private static Promotion Promotion(PromotionAccountUsagePolicy accountUsagePolicy) =>
        new()
        {
            PromotionCode = $"PROMO{Guid.NewGuid():N}"[..20],
            PromotionName = "Test promotion",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10,
            ValidFrom = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(2030, 12, 31, 0, 0, 0, TimeSpan.Zero),
            AccountUsagePolicy = accountUsagePolicy,
            Status = "Active"
        };

    private static Booking Booking(Guid userId, Guid promotionId, BookingStatus status) =>
        new()
        {
            UserId = userId,
            PromotionId = promotionId,
            BookingCode = $"BK{Guid.NewGuid():N}"[..20],
            ContactName = "Test User",
            BookingStatus = status
        };
}
