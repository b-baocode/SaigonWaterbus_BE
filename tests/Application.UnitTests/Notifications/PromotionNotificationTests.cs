using NUnit.Framework;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Notifications;

public class PromotionNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);

    private static CreatePromotionCommand CreateCommand(
        PromotionStatus status,
        PromotionVisibility visibility = PromotionVisibility.Public,
        string code = "SUMMER25") =>
        new(
            code,
            "Hè rực rỡ",
            PromotionType.Percent,
            DiscountValue: 25,
            MaxDiscountAmount: 50000,
            MinOrderValue: null,
            ValidFrom: Now,
            ValidTo: Now.AddMonths(1),
            UsageLimit: null,
            MaxUsesPerAccount: null,
            BudgetCap: null,
            Visibility: visibility,
            Status: status);

    private static UpdatePromotionCommand UpdateCommand(
        Guid promotionId,
        PromotionStatus status,
        PromotionVisibility visibility = PromotionVisibility.Public) =>
        new(
            promotionId,
            "Hè rực rỡ",
            DiscountValue: 25,
            MaxDiscountAmount: 50000,
            MinOrderValue: null,
            ValidFrom: Now,
            ValidTo: Now.AddMonths(1),
            UsageLimit: null,
            MaxUsesPerAccount: null,
            BudgetCap: null,
            FirstBookingOnly: false,
            Scope: null,
            Visibility: visibility,
            Status: status);

    private static async Task<Guid> SeedSuspendedCustomerAsync(
        Infrastructure.Data.ApplicationDbContext context)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Suspended customer",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Suspended
        };
        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    [Test]
    public async Task CreatingActivePublicPromotionNotifiesActiveCustomersOnly()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer1 = await SeatFlowTestData.SeedCustomerAsync(context);
        var customer2 = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeatFlowTestData.SeedStaffAsync(context);
        await SeedSuspendedCustomerAsync(context);

        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var handler = new CreatePromotionCommandHandler(
            context, new FixedTimeProvider(Now), notificationRealtimeNotifier: realtimeNotifier);
        var dto = await handler.Handle(CreateCommand(PromotionStatus.Active), CancellationToken.None);

        var notifications = context.Set<Notification>().ToList();
        notifications.Count.ShouldBe(2);
        realtimeNotifier.Published.Count.ShouldBe(2);
        realtimeNotifier.Published.ShouldAllBe(x => x.Type == "promotion_new");
        notifications.Select(n => n.UserId).ShouldBe(
            [customer1.UserId!.Value, customer2.UserId!.Value], ignoreOrder: true);
        notifications.ShouldAllBe(n => n.Type == "promotion_new");
        notifications.ShouldAllBe(n => n.RelatedEntityType == "promotion");
        notifications.ShouldAllBe(n => n.RelatedEntityId == dto.PromotionId);
        var body = notifications[0].Body;
        body.ShouldNotBeNull();
        body.ShouldContain("SUMMER25");
        body.ShouldContain("25%");
    }

    [Test]
    public async Task DraftPromotionIsAnnouncedOnActivationExactlyOnce()
    {
        await using var context = SeatFlowTestData.CreateContext();
        await SeatFlowTestData.SeedCustomerAsync(context);

        var createHandler = new CreatePromotionCommandHandler(context, new FixedTimeProvider(Now));
        var dto = await createHandler.Handle(CreateCommand(PromotionStatus.Draft), CancellationToken.None);
        context.Set<Notification>().Count().ShouldBe(0);

        var updateHandler = new UpdatePromotionCommandHandler(context, new FixedTimeProvider(Now));
        await updateHandler.Handle(UpdateCommand(dto.PromotionId, PromotionStatus.Active), CancellationToken.None);
        context.Set<Notification>().Count().ShouldBe(1);

        // Sửa tiếp khi đang Active, rồi Paused → Active lại: không broadcast trùng.
        await updateHandler.Handle(UpdateCommand(dto.PromotionId, PromotionStatus.Active), CancellationToken.None);
        await updateHandler.Handle(UpdateCommand(dto.PromotionId, PromotionStatus.Paused), CancellationToken.None);
        await updateHandler.Handle(UpdateCommand(dto.PromotionId, PromotionStatus.Active), CancellationToken.None);
        context.Set<Notification>().Count().ShouldBe(1);
    }

    [Test]
    public async Task PrivatePromotionIsAnnouncedOnlyWhenMadePublic()
    {
        await using var context = SeatFlowTestData.CreateContext();
        await SeatFlowTestData.SeedCustomerAsync(context);

        var createHandler = new CreatePromotionCommandHandler(context, new FixedTimeProvider(Now));
        var dto = await createHandler.Handle(
            CreateCommand(PromotionStatus.Active, PromotionVisibility.Private, "SECRET10"),
            CancellationToken.None);
        context.Set<Notification>().Count().ShouldBe(0);

        var updateHandler = new UpdatePromotionCommandHandler(context, new FixedTimeProvider(Now));
        await updateHandler.Handle(
            UpdateCommand(dto.PromotionId, PromotionStatus.Active, PromotionVisibility.Public),
            CancellationToken.None);

        var notification = context.Set<Notification>().Single();
        notification.Type.ShouldBe("promotion_new");
        notification.Title.ShouldBe("Khuyến mãi mới");
    }
}
