using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SaigonWaterbus.Application.Operations;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Operations;

public class OperationScheduleSynchronizerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SyncIsNoOpSinceOperationScheduleEntryWasRemoved()
    {
        await using var context = SeatFlowTestData.CreateContext();

        var synchronizer = CreateSynchronizer(context);
        var count = await synchronizer.SyncAsync(FixedNow.AddDays(-1), FixedNow.AddDays(1), CancellationToken.None);

        count.ShouldBe(0);
    }

    private static OperationScheduleSynchronizer CreateSynchronizer(Infrastructure.Data.ApplicationDbContext context) =>
        new(
            context,
            new FixedTimeProvider(FixedNow),
            NullLogger<OperationScheduleSynchronizer>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
