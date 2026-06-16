using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Vessels;

public class VesselSeatFlowIntegrationTests
{
    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public async Task CreateVesselDoesNotRequireService(SeatSetupType setupType)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);

        var result = await CreateVesselUseCase(context, userContext).ExecuteAsync(
            CreateRequest(setupType),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(setupType);
        result.Status.ShouldBe(VesselStatus.Inactive);
        result.SeatsConfigured.ShouldBeFalse();
    }

    [Test]
    public async Task UpdateSeatSetupTypeBeforeLayoutIsAllowed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.FullStandard);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await UpdateVesselUseCase(context, userContext).ExecuteAsync(
            new UpdateVesselRequest(
                vessel.Id,
                SeatSetupType: SeatSetupType.StandardAndVip),
            CancellationToken.None);

        result.SeatSetupType.ShouldBe(SeatSetupType.StandardAndVip);
    }

    [Test]
    public async Task UpdateSeatSetupTypeRejectsVesselWithExistingLayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.FullStandard,
            seatsConfigured: true);
        context.Add(vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            UpdateVesselUseCase(context, userContext).ExecuteAsync(
                new UpdateVesselRequest(
                    vessel.Id,
                    SeatSetupType: SeatSetupType.StandardAndVip),
                CancellationToken.None));

        vessel.SeatSetupType.ShouldBe(SeatSetupType.FullStandard);
    }

    [Test]
    public async Task ActivateRejectsVesselWithoutConfiguredSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        context.Add(vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ValidationException>(() =>
            new UpdateVesselStatusRequestUseCase(context, userContext).ExecuteAsync(
                new UpdateVesselStatusRequest(vessel.Id, VesselStatus.Active),
                CancellationToken.None));

        vessel.Status.ShouldBe(VesselStatus.Inactive);
    }

    [Test]
    public async Task ActivateAcceptsConfiguredVesselWithoutService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await new UpdateVesselStatusRequestUseCase(context, userContext)
            .ExecuteAsync(
                new UpdateVesselStatusRequest(vessel.Id, VesselStatus.Active),
                CancellationToken.None);

        result.Status.ShouldBe(VesselStatus.Active);
        result.IsReadyForOperation.ShouldBeTrue();
    }

    [Test]
    public async Task GetVesselsCanSearchByVesselId()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var vessel = SeatFlowTestData.Vessel(SeatSetupType.StandardAndVip);
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await new GetVesselsRequestUseCase(context, userContext)
            .ExecuteAsync(new GetVesselsRequest(Search: vessel.Id.ToString()), CancellationToken.None);

        result.Single().Id.ShouldBe(vessel.Id);
    }

    private static CreateVesselRequestUseCase CreateVesselUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestVesselImageStorageService());

    private static UpdateVesselRequestUseCase UpdateVesselUseCase(
        Infrastructure.Data.ApplicationDbContext context,
        TestUserContext userContext) =>
        new(
            context,
            userContext,
            new TestDatabaseExceptionClassifier(),
            new TestVesselImageStorageService());

    private static CreateVesselRequest CreateRequest(SeatSetupType setupType) =>
        new(
            $"NEW_{Guid.NewGuid():N}"[..20],
            "New vessel",
            VesselStatus.Inactive,
            4,
            1,
            SeatSetupType: setupType);
}
