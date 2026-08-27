using NUnit.Framework;
using FluentValidation;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.InsurancePackages;

public class InsurancePackageProviderSourceTests
{
    private const string BookingType = "PassengerInsurance";

    // Handlers are invoked directly in unit tests (project convention — see
    // CreateCharterBookingCommandTests). FluentValidation runs in the MediatR pipeline at
    // runtime via ValidationBehaviour<,>, so we replicate it here by invoking validators
    // manually before the handler. Same exception type, same camelCase key convention.
    private static CreateInsurancePackageCommandHandler CreateHandler(ApplicationDbContext context) =>
        new(context);

    private static UpdateInsurancePackageCommandHandler CreateUpdateHandler(ApplicationDbContext context) =>
        new(context);

    private static async Task ValidateAsync<T>(
        IValidator<T> validator,
        T request,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    private static async Task<InsurancePackageDto> CreateAsync(
        CreateInsurancePackageCommandHandler handler,
        CreateInsurancePackageCommand request,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(new CreateInsurancePackageCommandValidator(), request, cancellationToken);
        return await handler.Handle(request, cancellationToken);
    }

    private static async Task<InsurancePackageDto> UpdateAsync(
        UpdateInsurancePackageCommandHandler handler,
        UpdateInsurancePackageCommand request,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(new UpdateInsurancePackageCommandValidator(), request, cancellationToken);
        return await handler.Handle(request, cancellationToken);
    }

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

        var command = new CreateInsurancePackageCommand(
            Code: "WB001",
            Name: "Gói Waterbus thiếu flag",
            BookingType: BookingType,
            UnitPremiumAmount: 5000m,
            CoverageAmount: 100_000_000m,
            IsRequired: false,
            ProviderName: "Waterbus",
            Status: InsurancePackageStatus.Active,
            IsWaterbusDefault: false,
            ProviderSource: InsuranceProviderSource.Waterbus);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            CreateAsync(handler, command, CancellationToken.None));

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

        var result = await CreateAsync(
            handler,
            new CreateInsurancePackageCommand(
                Code: "WB002",
                Name: "Gói Waterbus default",
                BookingType: BookingType,
                UnitPremiumAmount: 5000m,
                CoverageAmount: 100_000_000m,
                IsRequired: false,
                ProviderName: "Waterbus",
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

        var result = await CreateAsync(
            handler,
            new CreateInsurancePackageCommand(
                Code: "TP001",
                Name: "Gói bên thứ 3",
                BookingType: BookingType,
                UnitPremiumAmount: 3000m,
                CoverageAmount: 50_000_000m,
                IsRequired: false,
                ProviderName: "Bảo hiểm đối tác",
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

        var command = new CreateInsurancePackageCommand(
            Code: "WB_NEW",
            Name: "Gói Waterbus thứ 2",
            BookingType: BookingType,
            UnitPremiumAmount: 5000m,
            CoverageAmount: 100_000_000m,
            IsRequired: false,
            ProviderName: "Waterbus",
            Status: InsurancePackageStatus.Active,
            IsWaterbusDefault: true,
            ProviderSource: InsuranceProviderSource.Waterbus);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            CreateAsync(handler, command, CancellationToken.None));

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

        var result = await UpdateAsync(
            handler,
            new UpdateInsurancePackageCommand(
                InsurancePackageId: package.Id,
                Name: package.Name,
                BookingType: BookingType,
                UnitPremiumAmount: package.UnitPremiumAmount,
                CoverageAmount: package.CoverageAmount,
                IsRequired: false,
                ProviderName: "Waterbus",
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

        var result = await UpdateAsync(
            handler,
            new UpdateInsurancePackageCommand(
                InsurancePackageId: package.Id,
                Name: package.Name,
                BookingType: BookingType,
                UnitPremiumAmount: package.UnitPremiumAmount,
                CoverageAmount: package.CoverageAmount,
                IsRequired: false,
                ProviderName: "Bảo hiểm đối tác",
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

        var command = new UpdateInsurancePackageCommand(
            InsurancePackageId: toBeSwitched.Id,
            Name: toBeSwitched.Name,
            BookingType: BookingType,
            UnitPremiumAmount: toBeSwitched.UnitPremiumAmount,
            CoverageAmount: toBeSwitched.CoverageAmount,
            IsRequired: false,
            ProviderName: "Waterbus",
            ProviderLogoUrl: null,
            ImageUrl: null,
            Conditions: null,
            TermsUrl: null,
            Status: InsurancePackageStatus.Active,
            RewardOption: null,
            ProviderSource: InsuranceProviderSource.Waterbus);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            UpdateAsync(handler, command, CancellationToken.None));

        // Either rule may fire first — both indicate the same invariant.
        var hasWaterbusDefaultError = exception.Errors
            .Any(kv => kv.Value.Any(msg => msg.Contains("1 gói Waterbus default", StringComparison.Ordinal)));
        var hasProviderSourceError = exception.Errors
            .Any(kv => kv.Key.Equals("providerSource", StringComparison.Ordinal)
                && kv.Value.Any(msg => msg.Contains("1 gói Waterbus", StringComparison.Ordinal)));
        (hasWaterbusDefaultError || hasProviderSourceError).ShouldBeTrue();
    }
}
