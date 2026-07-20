using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Domain.Entities;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Integration.Tests;

/// <summary>
/// Race đặt trùng ghế — chỉ đo được trên Postgres thật: provider in-memory của unit test không có
/// khoá hàng nên SELECT … FOR UPDATE bị bỏ qua hoàn toàn.
/// </summary>
public class SeatBookingConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 1, 0, 0, TimeSpan.Zero);

    private PostgresTestDatabase _database = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync() => _database = await PostgresTestDatabase.CreateAsync();

    [OneTimeTearDown]
    public async Task TearDownAsync() => await _database.DisposeAsync();

    [Test]
    public async Task ConcurrentBookingsOnSameSeatAndSegmentLeaveExactlyOneTicket()
    {
        const string tripCode = "TRRACE01";
        Guid firstUserId;
        Guid secondUserId;
        SeededTrip trip;

        await using (var setupContext = _database.CreateContext())
        {
            firstUserId = await SeatBookingTestData.SeedCustomerAsync(setupContext, "1");
            secondUserId = await SeatBookingTestData.SeedCustomerAsync(setupContext, "2");
            trip = await SeatBookingTestData.SeedTripAsync(setupContext, Now, tripCode);
        }

        // Hai DbContext riêng = hai connection riêng; dùng chung một context sẽ không có
        // concurrency thật vì mọi lệnh đi tuần tự trên cùng một connection.
        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();

        var command = new CreateBookingCommand(
            tripCode,
            [BookItem(tripCode)],
            null);

        var firstAttempt = CreateHandler(firstContext, firstUserId).Handle(command, CancellationToken.None);
        var secondAttempt = CreateHandler(secondContext, secondUserId).Handle(command, CancellationToken.None);

        var outcomes = await Task.WhenAll(
            CaptureAsync(firstAttempt),
            CaptureAsync(secondAttempt));

        var succeeded = outcomes.Count(x => x.Succeeded);
        var rejected = outcomes.Where(x => !x.Succeeded).ToList();
        var diagnostics = string.Join(" || ", outcomes.Select(x => x.Succeeded ? "OK" : x.Error));

        succeeded.ShouldBe(1,
            $"đúng một request được giữ ghế, request còn lại phải bị từ chối. Kết quả: {diagnostics}");
        rejected.Count.ShouldBe(1);
        rejected[0].Error.ShouldNotBeNull()
            .ShouldContain("already booked", Case.Insensitive,
                "request thua phải nhận lỗi ghế đã bán, không phải lỗi hạ tầng");

        // Và quan trọng nhất: DB chỉ có đúng một vé chiếm ghế đó.
        await using var assertContext = _database.CreateContext();
        var passengerCount = await assertContext.Set<BookingPassenger>()
            .CountAsync(p => p.TripSeatId == trip.TripSeatId);
        passengerCount.ShouldBe(1);
    }

    private static BookingItemRequest BookItem(string tripCode) =>
        new(
            "A1",
            "ADULT",
            SeatBookingTestData.FromStationCode(tripCode),
            SeatBookingTestData.ToStationCode(tripCode),
            "Nguyen Van A",
            null, null, null, null, null);

    private static CreateBookingCommandHandler CreateHandler(
        Infrastructure.Data.ApplicationDbContext context,
        Guid userId) =>
        new(
            context,
            new TestUserContext(userId),
            new GuidBookingCodeGenerator(),
            new FixedFareCalculator(10000m),
            new FixedTimeProvider(Now));

    private static async Task<(bool Succeeded, string? Error)> CaptureAsync(Task<CreateBookingResult> attempt)
    {
        try
        {
            await attempt;
            return (true, null);
        }
        catch (ValidationException ex)
        {
            return (false, string.Join(" | ", ex.Errors.SelectMany(x => x.Value)));
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
