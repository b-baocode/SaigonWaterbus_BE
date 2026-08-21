using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Tickets;

/// <summary>
/// Chạy CHÍNH con job nền trên dữ liệu thật trong DB, không phải gọi lẻ từng hàm — để chắc
/// chắn cả đường dây (query + Include + điều kiện) hoạt động đúng.
///
/// Mỗi test đều kèm một VÉ ĐỐI CHỨNG chắc chắn phải hết hạn. Vé đó đổi trạng thái chính là
/// bằng chứng job đã chạy xong; không có nó thì "vé khứ hồi còn Active" có thể chỉ vì job
/// chưa kịp làm gì.
/// </summary>
public class TicketExpirationHostedServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 10, 45, 0, TimeSpan.Zero);

    /// <summary>Tên database in-memory của test đang chạy — job và test dùng chung dữ liệu.</summary>
    private string _databaseName = string.Empty;

    /// <summary>
    /// BẮT BUỘC chia sẻ root: chỉ trùng TÊN database là chưa đủ, EF InMemory có thể dựng kho
    /// riêng cho mỗi options builder và job sẽ soi vào một database rỗng.
    /// </summary>
    private InMemoryDatabaseRoot _databaseRoot = new();

    [SetUp]
    public void SetUp()
    {
        _databaseName = $"ticket-expiry-{Guid.NewGuid():N}";
        _databaseRoot = new InMemoryDatabaseRoot();
    }

    /// <summary>
    /// Mỗi lần gọi là một DbContext MỚI trên cùng database. Job chạy nền còn test thì đọc để kiểm
    /// tra, dùng chung một instance sẽ dính "A second operation was started on this context".
    /// </summary>
    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_databaseName, _databaseRoot)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Tái hiện đúng booking BK-20260821-DSL4L: khứ hồi, vừa thanh toán, hai chuyến còn hơn
    /// hai tiếng nữa mới chạy. Trước khi sửa, job giết sạch cả hai vé sau một phút.
    /// </summary>
    [Test]
    public async Task RoundTripTicketsSurviveTheJobBeforeDeparture()
    {
        await using var context = NewContext();
        var outbound = SeedTrip(context, Now.AddHours(2), "BB-OUTBOUND");
        var inbound = SeedTrip(context, Now.AddHours(4), "BB-INBOUND");
        var roundTrip = SeedBooking(context, outbound, inbound);
        var control = SeedDepartedControlBooking(context);
        await context.SaveChangesAsync(CancellationToken.None);

        await RunJobAsync();

        await using var verify = NewContext();
        var controlTicket = await ReloadAsync(verify, control);
        controlTicket.TicketStatus.ShouldBe(
            TicketStatus.Expired,
            "vé đối chứng phải hết hạn, nếu không thì job chưa chạy và test này vô nghĩa");

        var tickets = await verify.Tickets
            .Where(x => x.BookingId == roundTrip.Id)
            .ToListAsync();
        tickets.Count.ShouldBe(2);
        tickets.ShouldAllBe(x => x.TicketStatus == TicketStatus.Active);
    }

    /// <summary>
    /// Chiều đi đã chạy xong không được kéo vé chiều về chết theo — đây là nửa còn lại của bug
    /// (job xét ở mức booking nên lấy giờ chiều đi phán xét cả hai vé).
    /// </summary>
    [Test]
    public async Task ReturnLegSurvivesAfterOutboundTripHasSailed()
    {
        await using var context = NewContext();
        var outbound = SeedTrip(context, Now.AddHours(-2), "BB-OUTBOUND");
        MarkBoardingStopDeparted(outbound, Now.AddHours(-2));
        var inbound = SeedTrip(context, Now.AddHours(3), "BB-INBOUND");
        var booking = SeedBooking(context, outbound, inbound);
        await context.SaveChangesAsync(CancellationToken.None);

        await RunJobAsync();

        await using var verify = NewContext();
        var tickets = await verify.Tickets
            .Include(x => x.BookingPassenger)
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync();

        var outboundTicket = tickets.Single(x => x.BookingPassenger!.TripId == outbound.Id);
        var returnTicket = tickets.Single(x => x.BookingPassenger!.TripId == inbound.Id);

        outboundTicket.TicketStatus.ShouldBe(
            TicketStatus.Expired, "tàu chiều đi đã rời bến, khách không đến");
        returnTicket.TicketStatus.ShouldBe(
            TicketStatus.Active, "chuyến chiều về còn 3 tiếng nữa mới chạy");
    }

    private async Task RunJobAsync()
    {
        // Job phải có DbContext RIÊNG: nó chạy nền, còn test thì đọc để kiểm tra — dùng chung một
        // instance là dính "A second operation was started on this context".
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        await using var provider = services.BuildServiceProvider();

        var job = new TicketExpirationHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TicketExpirationHostedService>.Instance);

        // StartAsync trả về ngay khi ExecuteAsync gặp await đầu tiên, nên phải chờ lượt quét đầu
        // làm xong. Mốc dừng là vé đối chứng đổi trạng thái (hoặc hết giờ chờ).
        await job.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = NewContext();
            if (await probe.Tickets.AnyAsync(x => x.TicketStatus == TicketStatus.Expired))
            {
                break;
            }

            await Task.Delay(25);
        }

        await job.StopAsync(CancellationToken.None);
    }

    private static Task<Ticket> ReloadAsync(ApplicationDbContext context, Booking booking) =>
        context.Tickets.Where(x => x.BookingId == booking.Id).FirstAsync();

    private static Trip SeedTrip(ApplicationDbContext context, DateTimeOffset departure, string code)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(),
            RouteId = Guid.NewGuid(),
            TripCode = $"{code}-{Guid.NewGuid():N}"[..20],
            OperatingDate = DateOnly.FromDateTime(departure.UtcDateTime),
            DepartureTime = departure,
            ArrivalTime = departure.AddMinutes(50),
            TripStatus = TripStatus.Scheduled
        };

        for (var order = 1; order <= 2; order++)
        {
            trip.TripStops.Add(new TripStop
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                StationId = Guid.NewGuid(),
                StopOrder = order,
                StayDurationMinutes = 5,
                PlannedArrivalTime = departure.AddMinutes(50 * (order - 1)),
                PlannedDepartureTime = departure.AddMinutes(50 * (order - 1)),
                StopStatus = TripStopStatuses.Scheduled
            });
        }

        context.Set<Trip>().Add(trip);
        return trip;
    }

    private static void MarkBoardingStopDeparted(Trip trip, DateTimeOffset at)
    {
        var boarding = trip.TripStops.OrderBy(x => x.StopOrder).First();
        boarding.StopStatus = TripStopStatuses.Departed;
        boarding.ActualArrivalTime = at.AddMinutes(-8);
        boarding.ActualDepartureTime = at;
    }

    /// <summary>Booking một chiều mà tàu đã rời bến từ lâu — chắc chắn phải bị expire.</summary>
    private static Booking SeedDepartedControlBooking(ApplicationDbContext context)
    {
        var trip = SeedTrip(context, Now.AddHours(-5), "BB-CONTROL");
        MarkBoardingStopDeparted(trip, Now.AddHours(-5));
        return SeedBooking(context, trip, returnTrip: null);
    }

    private static Booking SeedBooking(ApplicationDbContext context, Trip trip, Trip? returnTrip)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingCode = $"BK-{Guid.NewGuid():N}"[..16],
            TripId = trip.Id,
            ReturnTripId = returnTrip?.Id,
            ContactName = "Ngo Gia Bao",
            ContactPhone = "+84867654946",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid"
        };
        context.Set<Booking>().Add(booking);

        AddPassengerTicket(context, booking, trip);
        if (returnTrip is not null)
        {
            AddPassengerTicket(context, booking, returnTrip);
        }

        return booking;
    }

    private static void AddPassengerTicket(ApplicationDbContext context, Booking booking, Trip trip)
    {
        var stops = trip.TripStops.OrderBy(x => x.StopOrder).ToArray();
        var passenger = new BookingPassenger
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Ngo Gia Bao",
            TripId = trip.Id,
            FromStationId = stops.First().StationId,
            ToStationId = stops.Last().StationId,
            FromStopOrder = stops.First().StopOrder,
            ToStopOrder = stops.Last().StopOrder
        };
        context.Set<BookingPassenger>().Add(passenger);

        context.Tickets.Add(new Ticket
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            BookingPassengerId = passenger.Id,
            TicketCode = $"TK{Guid.NewGuid():N}"[..16],
            QrToken = Guid.NewGuid().ToString("N"),
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now.AddMinutes(-15)
        });
    }
}
