using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Notifications;

public class NotificationApiTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);

    private static Notification CreateNotification(
        Guid userId,
        DateTimeOffset createdAt,
        bool isRead = false,
        string title = "Thông báo") =>
        new()
        {
            UserId = userId,
            Title = title,
            Body = "Nội dung",
            Type = "booking_confirmed",
            IsRead = isRead,
            ReadAt = isRead ? createdAt : null,
            CreatedAt = createdAt
        };

    [Test]
    public async Task ListReturnsOwnNotificationsNewestFirstWithCounts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var oldest = CreateNotification(me, Now.AddMinutes(-30), isRead: true, title: "Cũ nhất");
        var middle = CreateNotification(me, Now.AddMinutes(-20), title: "Giữa");
        var newest = CreateNotification(me, Now.AddMinutes(-10), title: "Mới nhất");
        context.AddRange(oldest, middle, newest, CreateNotification(someoneElse, Now));
        await context.SaveChangesAsync();

        var handler = new GetMyNotificationsQueryHandler(context, new TestUserContext(me));
        var page1 = await handler.Handle(new GetMyNotificationsQuery(Page: 1, PageSize: 2), CancellationToken.None);

        page1.TotalCount.ShouldBe(3);
        page1.UnreadCount.ShouldBe(2);
        page1.Items.Count.ShouldBe(2);
        page1.Items[0].NotificationId.ShouldBe(newest.Id);
        page1.Items[1].NotificationId.ShouldBe(middle.Id);

        var page2 = await handler.Handle(new GetMyNotificationsQuery(Page: 2, PageSize: 2), CancellationToken.None);
        page2.Items.Count.ShouldBe(1);
        page2.Items[0].NotificationId.ShouldBe(oldest.Id);

        var unreadOnly = await handler.Handle(
            new GetMyNotificationsQuery(UnreadOnly: true), CancellationToken.None);
        unreadOnly.TotalCount.ShouldBe(2);
        unreadOnly.UnreadCount.ShouldBe(2);
        unreadOnly.Items.ShouldAllBe(x => !x.IsRead);
    }

    [Test]
    public async Task UnreadCountCountsOnlyMyUnreadNotifications()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var me = Guid.NewGuid();
        context.AddRange(
            CreateNotification(me, Now.AddMinutes(-3)),
            CreateNotification(me, Now.AddMinutes(-2), isRead: true),
            CreateNotification(Guid.NewGuid(), Now.AddMinutes(-1)));
        await context.SaveChangesAsync();

        var handler = new GetMyUnreadNotificationCountQueryHandler(context, new TestUserContext(me));
        var result = await handler.Handle(new GetMyUnreadNotificationCountQuery(), CancellationToken.None);

        result.UnreadCount.ShouldBe(1);
    }

    [Test]
    public async Task MarkReadSetsReadAtOnceAndHidesOtherUsersNotifications()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var me = Guid.NewGuid();
        var mine = CreateNotification(me, Now.AddMinutes(-5));
        var someoneElses = CreateNotification(Guid.NewGuid(), Now.AddMinutes(-5));
        context.AddRange(mine, someoneElses);
        await context.SaveChangesAsync();

        var handler = new MarkNotificationReadCommandHandler(
            context, new TestUserContext(me), new FixedTimeProvider(Now));
        var dto = await handler.Handle(new MarkNotificationReadCommand(mine.Id), CancellationToken.None);

        dto.IsRead.ShouldBeTrue();
        dto.ReadAt.ShouldBe(Now);
        mine.IsRead.ShouldBeTrue();
        mine.ReadAt.ShouldBe(Now);

        // Idempotent: gọi lại không đổi readAt.
        var laterHandler = new MarkNotificationReadCommandHandler(
            context, new TestUserContext(me), new FixedTimeProvider(Now.AddHours(1)));
        var again = await laterHandler.Handle(new MarkNotificationReadCommand(mine.Id), CancellationToken.None);
        again.ReadAt.ShouldBe(Now);

        // Thông báo của người khác → 404, không lộ sự tồn tại.
        await Should.ThrowAsync<NotFoundException>(() =>
            handler.Handle(new MarkNotificationReadCommand(someoneElses.Id), CancellationToken.None));
    }

    [Test]
    public async Task MarkAllReadMarksOnlyMineAndIsIdempotent()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var me = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var otherUsersNotification = CreateNotification(otherUser, Now.AddMinutes(-1));
        context.AddRange(
            CreateNotification(me, Now.AddMinutes(-3)),
            CreateNotification(me, Now.AddMinutes(-2)),
            CreateNotification(me, Now.AddMinutes(-1), isRead: true),
            otherUsersNotification);
        await context.SaveChangesAsync();

        var handler = new MarkAllMyNotificationsReadCommandHandler(
            context, new TestUserContext(me), new FixedTimeProvider(Now));
        var result = await handler.Handle(new MarkAllMyNotificationsReadCommand(), CancellationToken.None);

        result.MarkedCount.ShouldBe(2);
        context.Set<Notification>().Where(n => n.UserId == me).ToList()
            .ShouldAllBe(n => n.IsRead);
        otherUsersNotification.IsRead.ShouldBeFalse();

        var second = await handler.Handle(new MarkAllMyNotificationsReadCommand(), CancellationToken.None);
        second.MarkedCount.ShouldBe(0);
    }
}
