using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.InsurancePackages;

public class InsurancePackageProviderSourceTests
{
    private const string BookingType = "PassengerInsurance";

    private static CreateInsurancePackageCommandHandler CreateHandler(ApplicationDbContext context) =>
        new(context, new CreateInsurancePackageCommandValidator());

    private static UpdateInsurancePackageCommandHandler CreateUpdateHandler(ApplicationDbContext context) =>
        new(context, new UpdateInsurancePackageCommandValidator());

    private static UpdateInsurancePackageStatusCommandHandler CreateStatusHandler(ApplicationDbContext context) =>
        new(context);

    private static InsurancePackage SeedWaterbusActive(
        ApplicationDbContext context,
        string code,
        bool isWaterbusDefault = true)
    {
        var package = new InsurancePackage
        {
            Code = code,
            Name = $"{code} package",
            BookingType = BookingType,
            UnitPremiumAmount = 5000m,
            CoverageAmount = 100_000_000m,
            Currency = "VND",
            IsActive = true,
            IsWaterbusDefault = isWaterbusDefault,
            ProviderSource = InsuranceProviderSource.Waterbus
        };
        context.Add(package);
        return package;
    }

    // -------------------------------------------------------------------------
    // CREATE — Validator rule: ProviderSource=Waterbus => IsWaterbusDefault=true
    // -------------------------------------------------------------------------

    [Test]
    public async Task Create_WithWaterbusSource_ButNotWaterbusDefault_ThrowsValidation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var handler = CreateHandler(context);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateInsurancePackageCommand(
                    Code: "WB001",
                    Name: "Gói Waterbus thiếu flag",
                    BookingType: BookingType,
                    UnitPremiumAmount: 5000m,
                    CoverageAmount: 100_000_000m,
                    IsRequired: false,
                    Status: InsurancePackageStatus.Active,
                    IsWaterbusDefault: false,
                    ProviderSource: InsuranceProviderSource.Waterbus),
                CancellationToken.None));

        var hasIsWaterbusDefaultError = exception.Errors
            .Any(kv => kv.Key.Equals("isWaterbusDefault", StringComparison.Ordinal)
                && kv.Value.Any(msg => msg.Contains("IsWaterbusDefault phải là true", StringComparison.Ordinal)));
        hasIsWaterbusDefaultError.ShouldBeTrue();
    }

    [Test]
    public async Task Create_WithWaterbusSource_AndWaterbusDefault_PersistsWaterbus()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateInsurancePackageCommand(
                Code: "WB002",
                Name: "Gói Waterbus default",
                BookingType: BookingType,
                UnitPremiumAmount: 5000m,
                CoverageAmount: 100_000_000m,
                IsRequired: false,
                Status: InsurancePackageStatus.Active,
                IsWaterbusDefault: true,
                ProviderSource: InsuranceProviderSource.Waterbus),
            CancellationToken.None);

        result.ProviderSource.ShouldBe(InsuranceProviderSource.Waterbus);
        result.IsWaterbusDefault.ShouldBeTrue();

        var stored = context.Set<InsurancePackage>().Single();
        stored.ProviderSource.ShouldBe(InsuranceProviderSource.Waterbus);
        stored.IsWaterbusDefault.ShouldBeTrue();
    }

    [Test]
    public async Task Create_WithThirdPartySource_DefaultsToThirdParty()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateInsurancePackageCommand(
                Code: "TP001",
                Name: "Gói bên thứ 3",
                BookingType: BookingType,
                UnitPremiumAmount: 3000m,
                CoverageAmount: 50_000_000m,
                IsRequired: false,
                Status: InsurancePackageStatus.Active),
            CancellationToken.None);

        result.ProviderSource.ShouldBe(InsuranceProviderSource.ThirdParty);
        result.IsWaterbusDefault.ShouldBeFalse();

        var stored = context.Set<InsurancePackage>().Single();
        stored.ProviderSource.ShouldBe(InsuranceProviderSource.ThirdParty);
        stored.IsWaterbusDefault.ShouldBeFalse();
    }

    [Test]
    public async Task Create_WithWaterbusSource_WhenAnotherActiveWaterbusExists_ThrowsValidation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        SeedWaterbusActive(context, "WB_EXIST");
        await context.SaveChangesAsync();

        var handler = CreateHandler(context);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateInsurancePackageCommand(
                    Code: "WB_NEW",
                    Name: "Gói Waterbus thứ 2",
                    BookingType: BookingType,
                    UnitPremiumAmount: 5000m,
                    CoverageAmount: 100_000_000m,
                    IsRequired: false,
                    Status: InsurancePackageStatus.Active,
                    IsWaterbusDefault: true,
                    ProviderSource: InsuranceProviderSource.Waterbus),
                CancellationToken.None));

        // Either rule may fire first depending on enumeration order — both indicate the same
        // business invariant ("only one active Waterbus package per booking type").
        var hasWaterbusDefaultError = exception.Errors
            .Any(kv => kv.Value.Any(msg => msg.Contains("1 gói Waterbus default", StringComparison.Ordinal)));
        var hasProviderSourceError = exception.Errors
            .Any(kv => kv.Key.Equals("providerSource", StringComparison.Ordinal)
                && kv.Value.Any(msg => msg.Contains("1 gói Waterbus", StringComparison.Ordinal)));
        (hasWaterbusDefaultError || hasProviderSourceError).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // UPDATE — Switching two-way between ThirdParty and Waterbus
    // -------------------------------------------------------------------------

    [Test]
    public async Task Update_FromThirdParty_ToWaterbus_PersistsAndMarksWaterbusDefault()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var package = new InsurancePackage
        {
            Code = "TP_TO_WB",
            Name = "Gói sẽ chuyển sang Waterbus",
            BookingType = BookingType,
            UnitPremiumAmount = 3000m,
            CoverageAmount = 50_000_000m,
            Currency = "VND",
            IsActive = true,
            IsWaterbusDefault = false,
            ProviderSource = InsuranceProviderSource.ThirdParty
        };
        context.Add(package);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context);

        var result = await handler.Handle(
            new UpdateInsurancePackageCommand(
                InsurancePackageId: package.Id,
                Name: package.Name,
                BookingType: BookingType,
                UnitPremiumAmount: package.UnitPremiumAmount,
                CoverageAmount: package.CoverageAmount,
                IsRequired: false,
                ProviderName: null,
                ProviderLogoUrl: null,
                ImageUrl: null,
                Conditions: null,
                TermsUrl: null,
                Status: InsurancePackageStatus.Active,
                RewardOption: null,
                ProviderSource: InsuranceProviderSource.Waterbus),
            CancellationToken.None);

        result.ProviderSource.ShouldBe(InsuranceProviderSource.Waterbus);
        result.IsWaterbusDefault.ShouldBeTrue();

        var stored = context.Set<InsurancePackage>().Single();
        stored.ProviderSource.ShouldBe(InsuranceProviderSource.Waterbus);
        stored.IsWaterbusDefault.ShouldBeTrue();
        stored.IsActive.ShouldBeTrue();
    }

    [Test]
    public async Task Update_FromWaterbus_ToThirdParty_ClearsWaterbusDefault()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var package = SeedWaterbusActive(context, "WB_TO_TP");
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context);

        var result = await handler.Handle(
            new UpdateInsurancePackageCommand(
                InsurancePackageId: package.Id,
                Name: package.Name,
                BookingType: BookingType,
                UnitPremiumAmount: package.UnitPremiumAmount,
                CoverageAmount: package.CoverageAmount,
                IsRequired: false,
                ProviderName: null,
                ProviderLogoUrl: null,
                ImageUrl: null,
                Conditions: null,
                TermsUrl: null,
                Status: InsurancePackageStatus.Active,
                RewardOption: null,
                ProviderSource: InsuranceProviderSource.ThirdParty),
            CancellationToken.None);

        result.ProviderSource.ShouldBe(InsuranceProviderSource.ThirdParty);
        result.IsWaterbusDefault.ShouldBeFalse();

        var stored = context.Set<InsurancePackage>().Single();
        stored.ProviderSource.ShouldBe(InsuranceProviderSource.ThirdParty);
        stored.IsWaterbusDefault.ShouldBeFalse();
    }

    [Test]
    public async Task Update_ToWaterbus_WhileAnotherActiveWaterbusExists_ThrowsValidation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        SeedWaterbusActive(context, "WB_EXIST_2");
        var toBeSwitched = new InsurancePackage
        {
            Code = "TP_SWITCH",
            Name = "Gói sẽ chuyển",
            BookingType = BookingType,
            UnitPremiumAmount = 3000m,
            CoverageAmount = 50_000_000m,
            Currency = "VND",
            IsActive = true,
            IsWaterbusDefault = false,
            ProviderSource = InsuranceProviderSource.ThirdParty
        };
        context.Add(toBeSwitched);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateInsurancePackageCommand(
                    InsurancePackageId: toBeSwitched.Id,
                    Name: toBeSwitched.Name,
                    BookingType: BookingType,
                    UnitPremiumAmount: toBeSwitched.UnitPremiumAmount,
                    CoverageAmount: toBeSwitched.CoverageAmount,
                    IsRequired: false,
                    ProviderName: null,
                    ProviderLogoUrl: null,
                    ImageUrl: null,
                    Conditions: null,
                    TermsUrl: null,
                    Status: InsurancePackageStatus.Active,
                    RewardOption: null,
                    ProviderSource: InsuranceProviderSource.Waterbus),
                CancellationToken.None));

        // Either rule may fire first — both indicate the same invariant.
        var hasWaterbusDefaultError = exception.Errors
            .Any(kv => kv.Value.Any(msg => msg.Contains("1 gói Waterbus default", StringComparison.Ordinal)));
        var hasProviderSourceError = exception.Errors
            .Any(kv => kv.Key.Equals("providerSource", StringComparison.Ordinal)
                && kv.Value.Any(msg => msg.Contains("1 gói Waterbus", StringComparison.Ordinal)));
        (hasWaterbusDefaultError || hasProviderSourceError).ShouldBeTrue();
    }
}
