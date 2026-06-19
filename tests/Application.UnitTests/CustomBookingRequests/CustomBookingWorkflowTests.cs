using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookingRequests;

public class CustomBookingWorkflowTests
{
    [Test]
    public void StatusesContainOnlySupportedWorkflowStates()
    {
        Enum.GetValues<CustomBookingRequestStatus>().ShouldBe(
        [
            CustomBookingRequestStatus.PendingReview,
            CustomBookingRequestStatus.Quoted,
            CustomBookingRequestStatus.Cancelled,
            CustomBookingRequestStatus.Confirmed
        ]);
    }

    [Test]
    public void UpdateValidatorUsesSameTripRulesAsCreate()
    {
        var validator = new UpdateCustomBookingRequestCommandValidator();
        var command = ValidUpdateCommand() with
        {
            RequestedNumberOfDecks = 0,
            RequestedSeatSetupType = (SeatSetupType)999,
            AdultCount = 500,
            ChildCount = 1,
            SpecialRequests = new string('A', 1001)
        };

        var result = validator.Validate(command);

        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.RequestedNumberOfDecks));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.RequestedSeatSetupType));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.AdultCount));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.SpecialRequests));
    }

    [Test]
    public void AssignValidatorRequiresBothIds()
    {
        var result = new AssignCustomBookingVesselCommandValidator()
            .Validate(new AssignCustomBookingVesselCommand(Guid.Empty, Guid.Empty));

        result.Errors.ShouldContain(x => x.PropertyName == nameof(AssignCustomBookingVesselCommand.Id));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(AssignCustomBookingVesselCommand.VesselId));
    }

    [Test]
    public void CancelValidatorRequiresReason()
    {
        var result = new CancelCustomBookingRequestCommandValidator()
            .Validate(new CancelCustomBookingRequestCommand(Guid.NewGuid(), string.Empty));

        result.Errors.ShouldContain(x => x.PropertyName == nameof(CancelCustomBookingRequestCommand.Reason));
    }

    [Test]
    public void PricingValidatorRejectsInvalidCriteria()
    {
        var result = new GetCustomBookingPricingOptionsQueryValidator()
            .Validate(new GetCustomBookingPricingOptionsQuery(0, (SeatSetupType)999, (VesselRentalUnit)999, 0));

        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetCustomBookingPricingOptionsQuery.RequestedNumberOfDecks));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetCustomBookingPricingOptionsQuery.RequestedSeatSetupType));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetCustomBookingPricingOptionsQuery.RentalUnit));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetCustomBookingPricingOptionsQuery.PassengerCount));
    }

    [Test]
    public void DepartureMustBeAfterCurrentVietnamTime()
    {
        var now = new DateTimeOffset(2026, 6, 15, 3, 0, 0, TimeSpan.Zero);

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureDepartureIsInFuture(
                new DateOnly(2026, 6, 15),
                new TimeOnly(10, 0),
                now));

        exception.Errors.Keys.ShouldContain("departureDate");
    }

    [Test]
    public void QuoteRequiresAssignedVessel()
    {
        var request = ValidRequest();

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureCanQuote(request));

        exception.Errors.Keys.ShouldContain("assignedVesselId");
    }

    [Test]
    public void QuoteIsAllowedAfterAssigningMatchingVessel()
    {
        var request = ValidRequest();
        var vessel = ValidVessel();
        request.AssignedVesselId = vessel.Id;
        request.AssignedVessel = vessel;

        Should.NotThrow(() => CustomBookingRequestSupport.EnsureCanQuote(request));
        Should.NotThrow(() => CustomBookingRequestSupport.EnsureVesselMatchesRequest(request, vessel));
    }

    [TestCase(CustomBookingRequestStatus.PendingReview, true)]
    [TestCase(CustomBookingRequestStatus.Quoted, true)]
    [TestCase(CustomBookingRequestStatus.Confirmed, true)]
    [TestCase(CustomBookingRequestStatus.Cancelled, false)]
    public void CancelFollowsStateMachine(CustomBookingRequestStatus status, bool allowed)
    {
        var request = ValidRequest();
        request.Status = status;

        if (allowed)
        {
            Should.NotThrow(() => CustomBookingRequestSupport.EnsureCanCancel(request));
            return;
        }

        Should.Throw<ValidationException>(() => CustomBookingRequestSupport.EnsureCanCancel(request));
    }

    [Test]
    public void CustomerCannotEditAfterAdminAssignedVessel()
    {
        var request = ValidRequest();
        request.AssignedVesselId = Guid.NewGuid();

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureCanEdit(request));

        exception.Errors.Keys.ShouldContain("assignedVesselId");
    }

    [Test]
    public void VesselMustMatchDeckSeatTypeCapacityStatusAndPrice()
    {
        var request = ValidRequest();
        var vessel = ValidVessel();
        vessel.SeatCount = request.PassengerCount - 1;

        var exception = Should.Throw<ValidationException>(() =>
            CustomBookingRequestSupport.EnsureVesselMatchesRequest(request, vessel));

        exception.Errors.Keys.ShouldContain("assignedVesselId");
        exception.Errors["assignedVesselId"].Single().ShouldContain("thấp hơn yêu cầu");
    }

    [Test]
    public async Task PricingOptionsReturnRangesWithoutVesselDetails()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var first = ValidVessel("WB01", 12000000m);
        var second = ValidVessel("WB02", 15000000m);
        first.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = first.Id,
            Vessel = first,
            RentalUnit = VesselRentalUnit.Hour,
            UnitPrice = 2000000m,
            Currency = "VND"
        });
        second.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = second.Id,
            Vessel = second,
            RentalUnit = VesselRentalUnit.Hour,
            UnitPrice = 2500000m,
            Currency = "VND"
        });
        var wrongDeck = ValidVessel("WB03", 9000000m);
        wrongDeck.NumberOfDecks = 1;
        context.AddRange(first, second, wrongDeck);
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingPricingOptionsQueryHandler(context)
            .Handle(new GetCustomBookingPricingOptionsQuery(
                2,
                SeatSetupType.StandardAndVip,
                VesselRentalUnit.Hour,
                20), CancellationToken.None);

        result.MatchingVesselCount.ShouldBe(2);
        result.RentalUnit.ShouldBe(VesselRentalUnit.Hour);
        var hourlyRange = result.PriceRanges.Single(x => x.RentalUnit == VesselRentalUnit.Hour);
        hourlyRange.MinimumPrice.ShouldBe(2000000m);
        hourlyRange.MaximumPrice.ShouldBe(2500000m);
        result.PriceRanges.ShouldNotContain(x => x.RentalUnit == VesselRentalUnit.Day);
    }

    [Test]
    public async Task VesselRentalServiceDefaultsToActiveWaterTaxi()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seatBased = Service("WB", BookingMode.SeatBased, 1);
        var waterTaxi = Service("WT", BookingMode.VesselRental, 3);
        context.AddRange(seatBased, waterTaxi);
        await context.SaveChangesAsync();

        var result = await CustomBookingRequestSupport.ResolveVesselRentalServiceAsync(
            context,
            null,
            CancellationToken.None);

        result.Id.ShouldBe(waterTaxi.Id);
        result.BookingMode.ShouldBe(BookingMode.VesselRental);
    }

    [Test]
    public async Task VesselRentalServiceRejectsNonRentalService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var waterbus = Service("WB", BookingMode.SeatBased, 1);
        context.Add(waterbus);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            CustomBookingRequestSupport.ResolveVesselRentalServiceAsync(
                context,
                waterbus.Id,
                CancellationToken.None));

        exception.Errors.Keys.ShouldContain("serviceId");
    }

    [Test]
    public async Task RentalServicesQueryReturnsOnlyActiveVesselRentalServices()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var waterbus = Service("WB", BookingMode.SeatBased, 1);
        var activeRental = Service("WT", BookingMode.VesselRental, 2);
        var inactiveRental = Service("OLDWT", BookingMode.VesselRental, 3);
        inactiveRental.IsActive = false;
        context.AddRange(waterbus, activeRental, inactiveRental);
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingRentalServicesQueryHandler(context, adminContext)
            .Handle(new GetCustomBookingRentalServicesQuery(), CancellationToken.None);

        result.Select(x => x.Id).ShouldBe([activeRental.Id]);
        result.Single().BookingMode.ShouldBe(BookingMode.VesselRental);
    }

    [Test]
    public async Task AdminCandidateListContainsOnlyMatchingVessels()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        var matching = ValidVessel("WB01", 12000000m);
        var insufficient = ValidVessel("WB02", 10000000m);
        insufficient.SeatCount = request.PassengerCount - 1;
        context.AddRange(request, matching, insufficient);
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingVesselCandidatesQueryHandler(context, userContext)
            .Handle(new GetCustomBookingVesselCandidatesQuery(request.Id), CancellationToken.None);

        result.Count.ShouldBe(1);
        result.Single().VesselId.ShouldBe(matching.Id);
    }

    [Test]
    public async Task AdminCandidateListExcludesVesselsReservedByOverlappingCustomBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        request.EstimatedEndDate = request.DepartureDate;
        request.PreferredEndTime = new TimeOnly(13, 0);
        var reserved = ValidVessel("WB01", 12000000m);
        var available = ValidVessel("WB02", 12000000m);
        var overlapping = ValidRequest();
        overlapping.Status = CustomBookingRequestStatus.Quoted;
        overlapping.AssignedVesselId = reserved.Id;
        overlapping.AssignedVessel = reserved;
        overlapping.DepartureDate = request.DepartureDate;
        overlapping.PreferredStartTime = new TimeOnly(9, 0);
        overlapping.EstimatedEndDate = request.DepartureDate;
        overlapping.PreferredEndTime = new TimeOnly(10, 0);
        context.AddRange(request, reserved, available, overlapping);
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingVesselCandidatesQueryHandler(context, userContext)
            .Handle(new GetCustomBookingVesselCandidatesQuery(request.Id), CancellationToken.None);

        result.Select(x => x.VesselId).ShouldBe([available.Id]);
    }

    [Test]
    public async Task CustomerCanViewOwnCandidateVessels()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        var matching = ValidVessel("WB01", 12000000m);
        context.AddRange(request, matching);
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingVesselCandidatesQueryHandler(context, customerContext)
            .Handle(new GetCustomBookingVesselCandidatesQuery(request.Id), CancellationToken.None);

        result.Select(x => x.VesselId).ShouldBe([matching.Id]);
    }

    [Test]
    public async Task CustomerCannotViewAnotherCustomersCandidateVessels()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var ownerContext = await SeedCustomerAsync(context);
        var otherCustomerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = ownerContext.UserId;
        context.AddRange(request, ValidVessel("WB01", 12000000m));
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            new GetCustomBookingVesselCandidatesQueryHandler(context, otherCustomerContext)
                .Handle(new GetCustomBookingVesselCandidatesQuery(request.Id), CancellationToken.None));
    }

    [Test]
    public async Task CustomerCanSelectPreferredVesselFromCandidates()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        var vessel = ValidVessel("WB01", 12000000m);
        context.AddRange(request, vessel);
        await context.SaveChangesAsync();

        var result = await new SelectPreferredCustomBookingVesselCommandHandler(context, customerContext)
            .Handle(new SelectPreferredCustomBookingVesselCommand(request.Id, vessel.Id), CancellationToken.None);

        result.PreferredVessel.ShouldNotBeNull();
        result.PreferredVessel.Id.ShouldBe(vessel.Id);
        context.Set<CustomBookingRequest>().Single(x => x.Id == request.Id).PreferredVesselId.ShouldBe(vessel.Id);
    }

    [Test]
    public async Task CustomerCannotSelectPreferredVesselForAnotherCustomersRequest()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var ownerContext = await SeedCustomerAsync(context);
        var otherCustomerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = ownerContext.UserId;
        var vessel = ValidVessel("WB01", 12000000m);
        context.AddRange(request, vessel);
        await context.SaveChangesAsync();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            new SelectPreferredCustomBookingVesselCommandHandler(context, otherCustomerContext)
                .Handle(new SelectPreferredCustomBookingVesselCommand(request.Id, vessel.Id), CancellationToken.None));
    }

    [Test]
    public async Task CustomerCannotSelectPreferredVesselAfterAdminAssignedVessel()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.AssignedVesselId = Guid.NewGuid();
        var vessel = ValidVessel("WB01", 12000000m);
        context.AddRange(request, vessel);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new SelectPreferredCustomBookingVesselCommandHandler(context, customerContext)
                .Handle(new SelectPreferredCustomBookingVesselCommand(request.Id, vessel.Id), CancellationToken.None));

        exception.Errors.Keys.ShouldContain("assignedVesselId");
    }

    [Test]
    public async Task ManagerCandidatesContainOnlyActiveManagersAtDepartureStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var otherManagerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var departureStation = Station("FROM");
        var otherStation = Station("OTHER");
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.FromStationId = departureStation.Id;
        request.FromStation = departureStation;
        context.AddRange(departureStation, otherStation, request);
        context.AddRange(
            StationAssignment(managerContext.UserId!.Value, departureStation.Id, adminContext.UserId!.Value),
            StationAssignment(otherManagerContext.UserId!.Value, otherStation.Id, adminContext.UserId!.Value));
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingManagerCandidatesQueryHandler(context, adminContext)
            .Handle(new GetCustomBookingManagerCandidatesQuery(request.Id), CancellationToken.None);

        result.Select(x => x.UserId).ShouldBe([managerContext.UserId.Value]);
    }

    [Test]
    public async Task AssignedManagerCanPlanStaffAndOperationalServices()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var departureStation = Station("FROM");
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.FromStationId = departureStation.Id;
        request.FromStation = departureStation;
        request.AssignedManagerUserId = managerContext.UserId;
        context.AddRange(departureStation, request);
        context.AddRange(
            StationAssignment(managerContext.UserId!.Value, departureStation.Id, adminContext.UserId!.Value),
            StationAssignment(staffContext.UserId!.Value, departureStation.Id, adminContext.UserId!.Value));
        await context.SaveChangesAsync();

        var result = await new UpdateCustomBookingOperationPlanCommandHandler(
                context,
                managerContext,
                TimeProvider.System)
            .Handle(
                new UpdateCustomBookingOperationPlanCommand(
                    request.Id,
                    [new CustomBookingStaffPlanItem(staffContext.UserId.Value, "Đón khách")],
                    [new CustomBookingOperationServicePlanItem("Trang trí sinh nhật", 1, "Đã gồm trong báo giá")]),
                CancellationToken.None);

        result.StaffAssignments.Single().Staff.UserId.ShouldBe(staffContext.UserId.Value);
        result.StaffAssignments.Single().DutyNote.ShouldBe("Đón khách");
        result.OperationServices.Single().ServiceName.ShouldBe("Trang trí sinh nhật");
        result.OperationServices.Single().Quantity.ShouldBe(1);
    }

    [Test]
    public async Task AssignedManagerCannotUseStaffFromAnotherStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var departureStation = Station("FROM");
        var otherStation = Station("OTHER");
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.FromStationId = departureStation.Id;
        request.FromStation = departureStation;
        request.AssignedManagerUserId = managerContext.UserId;
        context.AddRange(departureStation, otherStation, request);
        context.AddRange(
            StationAssignment(managerContext.UserId!.Value, departureStation.Id, adminContext.UserId!.Value),
            StationAssignment(staffContext.UserId!.Value, otherStation.Id, adminContext.UserId!.Value));
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new UpdateCustomBookingOperationPlanCommandHandler(context, managerContext, TimeProvider.System)
                .Handle(
                    new UpdateCustomBookingOperationPlanCommand(
                        request.Id,
                        [new CustomBookingStaffPlanItem(staffContext.UserId.Value)],
                        []),
                    CancellationToken.None));

        exception.Errors.Keys.ShouldContain("staffAssignments");
    }

    [Test]
    public void HourlyRentalPriceUsesActualMinutes()
    {
        var request = ValidRequest();
        request.RentalUnit = VesselRentalUnit.Hour;
        request.EstimatedDurationMinutes = 130;
        var rentalPrice = new VesselRentalPrice
        {
            RentalUnit = VesselRentalUnit.Hour,
            UnitPrice = 2000000m,
            Currency = "VND"
        };

        var price = CustomBookingRequestSupport.CalculateRentalPrice(request, rentalPrice);

        price.ShouldBe(4333333.33m);
    }

    [Test]
    public async Task QuoteAddsServiceFeeToSelectedRentalUnitPrice()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.PendingReview;
        request.RentalUnit = VesselRentalUnit.Hour;
        request.EstimatedDurationMinutes = 125;
        var vessel = ValidVessel("WB01", 12000000m);
        vessel.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = vessel.Id,
            Vessel = vessel,
            RentalUnit = VesselRentalUnit.Hour,
            UnitPrice = 2000000m,
            Currency = "VND",
            Note = "Theo gio"
        });
        request.AssignedVesselId = vessel.Id;
        request.AssignedVessel = vessel;
        context.Add(request);
        await context.SaveChangesAsync();

        var result = await new QuoteCustomBookingRequestCommandHandler(
                context,
                adminContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero)),
                new TestCustomBookingQuoteEmailSender())
            .Handle(new QuoteCustomBookingRequestCommand(request.Id, 50, null, 400000m), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Quoted);
        result.Quote.ShouldNotBeNull();
        result.Quote.QuotedPrice.ShouldBe(433333m);
        result.Quote.ServiceFeeAmount.ShouldBe(400000m);
        result.Quote.DepositAmount.ShouldBe(216667m);
        result.Quote.RemainingAmount.ShouldBe(216666m);
        result.Quote.PriceNote.ShouldBe("Theo gio");
    }

    [Test]
    public async Task QuoteAppliesPromotionDiscountBeforeDepositCalculation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.PendingReview;
        request.RentalUnit = VesselRentalUnit.Day;
        var vessel = ValidVessel("WB01", 2000000m);
        var now = new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero);
        request.AssignedVesselId = vessel.Id;
        request.AssignedVessel = vessel;
        context.AddRange(
            request,
            new Promotion
            {
                PromotionCode = "TESTPAY",
                PromotionName = "Test thanh toan that",
                PromotionType = PromotionType.Fixed,
                DiscountValue = 1900000m,
                ValidFrom = now.AddDays(-1),
                ValidTo = now.AddDays(1),
                Status = "Active"
            });
        await context.SaveChangesAsync();

        var result = await new QuoteCustomBookingRequestCommandHandler(
                context,
                adminContext,
                new FixedTimeProvider(now),
                new TestCustomBookingQuoteEmailSender())
            .Handle(new QuoteCustomBookingRequestCommand(
                request.Id,
                50,
                null,
                0m,
                "TESTPAY"), CancellationToken.None);

        result.Quote.ShouldNotBeNull();
        result.Quote.QuotedPrice.ShouldBe(100000m);
        result.Quote.DiscountCode.ShouldBe("TESTPAY");
        result.Quote.DiscountAmount.ShouldBe(1900000m);
        result.Quote.DepositAmount.ShouldBe(50000m);
        result.Quote.RemainingAmount.ShouldBe(50000m);
    }

    [Test]
    public async Task PartialDepositQuoteExpiresAtRemainingPaymentDeadlineWhenSooner()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.PendingReview;
        var vessel = ValidVessel();
        request.AssignedVesselId = vessel.Id;
        request.AssignedVessel = vessel;
        context.Add(request);
        await context.SaveChangesAsync();

        var result = await new QuoteCustomBookingRequestCommandHandler(
                context,
                adminContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero)),
                new TestCustomBookingQuoteEmailSender())
            .Handle(new QuoteCustomBookingRequestCommand(request.Id, 50, null), CancellationToken.None);

        result.Quote.ShouldNotBeNull();
        result.Quote.ValidUntil.ShouldBe(new DateTimeOffset(2026, 6, 19, 1, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task QuoteWithinRemainingPaymentDeadlineRequiresFullDeposit()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.PendingReview;
        var vessel = ValidVessel();
        request.AssignedVesselId = vessel.Id;
        request.AssignedVessel = vessel;
        context.Add(request);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new QuoteCustomBookingRequestCommandHandler(
                    context,
                    adminContext,
                    new FixedTimeProvider(new DateTimeOffset(2026, 6, 19, 2, 0, 0, TimeSpan.Zero)),
                    new TestCustomBookingQuoteEmailSender())
                .Handle(new QuoteCustomBookingRequestCommand(request.Id, 50, null), CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .ShouldContain("Booking trong vòng 24 giờ trước khởi hành phải thanh toán 100% ngay khi chấp nhận báo giá.");
    }

    [Test]
    public async Task QuoteWithinRemainingPaymentDeadlineAllowsFullDeposit()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.PendingReview;
        var vessel = ValidVessel();
        request.AssignedVesselId = vessel.Id;
        request.AssignedVessel = vessel;
        context.Add(request);
        await context.SaveChangesAsync();

        var result = await new QuoteCustomBookingRequestCommandHandler(
                context,
                adminContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 19, 2, 0, 0, TimeSpan.Zero)),
                new TestCustomBookingQuoteEmailSender())
            .Handle(new QuoteCustomBookingRequestCommand(request.Id, 100, null), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Quoted);
        result.Quote.ShouldNotBeNull();
        result.Quote.DepositPercent.ShouldBe(100m);
        result.Quote.RemainingAmount.ShouldBe(0m);
    }

    [Test]
    public async Task AcceptPartialDepositQuoteAfterRemainingDeadlineIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            ValidUntil = new DateTimeOffset(2026, 6, 19, 5, 0, 0, TimeSpan.Zero)
        };
        context.Add(request);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new AcceptCustomBookingQuoteCommandHandler(
                    context,
                    customerContext,
                    new FixedTimeProvider(new DateTimeOffset(2026, 6, 19, 2, 0, 0, TimeSpan.Zero)),
                    new TestCustomBookingPaymentGateway(),
                    new TestCustomBookingQuoteEmailSender())
                .Handle(new AcceptCustomBookingQuoteCommand(request.Id), CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .ShouldContain("Booking đã quá hạn thanh toán phần còn lại trước 24 giờ khởi hành. Vui lòng liên hệ Admin để báo giá thanh toán 100%.");
    }

    [Test]
    public async Task AcceptQuoteConfirmsRequestWithoutIssuingQrTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            ValidUntil = now.AddHours(1)
        };
        context.Add(request);
        await context.SaveChangesAsync();

        var paymentGateway = new TestCustomBookingPaymentGateway();
        var result = await new AcceptCustomBookingQuoteCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway,
                new TestCustomBookingQuoteEmailSender())
            .Handle(new AcceptCustomBookingQuoteCommand(request.Id), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Quoted);
        result.Quote!.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        result.Quote.DepositPaymentCheckoutUrl.ShouldBe("https://payos.test/checkout");
        paymentGateway.CreatedDepositPayment.ShouldNotBeNull();
        paymentGateway.CreatedDepositPayment.Amount.ShouldBe(2500000);
        paymentGateway.CreatedDepositPayment.Description.ShouldBe($"260620{request.Id.ToString("N")[^3..].ToUpperInvariant()}");
        paymentGateway.CreatedDepositPayment.Description.Length.ShouldBe(9);
        paymentGateway.CreatedDepositPayment.ItemName.ShouldBe(
            $"Deposit booking CB-20260620-{request.Id.ToString("N")[^6..].ToUpperInvariant()}");
        result.Ticket.ShouldBeNull();
        context.CustomBookingTickets.Count().ShouldBe(0);
    }

    [Test]
    public async Task AcceptQuoteCanCreateFullPaymentLinkWhenCustomerChoosesFull()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            ValidUntil = now.AddHours(1)
        };
        context.Add(request);
        await context.SaveChangesAsync();

        var paymentGateway = new TestCustomBookingPaymentGateway();
        var result = await new AcceptCustomBookingQuoteCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway,
                new TestCustomBookingQuoteEmailSender())
            .Handle(
                new AcceptCustomBookingQuoteCommand(request.Id, CustomBookingQuotePaymentOption.Full),
                CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Quoted);
        result.Quote!.DepositPercent.ShouldBe(100m);
        result.Quote.DepositAmount.ShouldBe(5000000m);
        result.Quote.RemainingAmount.ShouldBe(0m);
        result.Quote.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        result.Quote.DepositPaymentCheckoutUrl.ShouldBe("https://payos.test/checkout");
        paymentGateway.CreatedDepositPayment.ShouldNotBeNull();
        paymentGateway.CreatedDepositPayment.Amount.ShouldBe(5000000);
        paymentGateway.CreatedDepositPayment.Description.ShouldBe($"260620{request.Id.ToString("N")[^3..].ToUpperInvariant()}");
        paymentGateway.CreatedDepositPayment.Description.Length.ShouldBe(9);
        paymentGateway.CreatedDepositPayment.ItemName.ShouldBe(
            $"Full payment CB-20260620-{request.Id.ToString("N")[^6..].ToUpperInvariant()}");
    }

    [Test]
    public async Task AcceptQuoteRecoversPayOsLinkWhenCreateCallFailsAfterRemoteCreation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            ValidUntil = now.AddHours(1)
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway
        {
            ThrowOnCreatePayment = true
        };

        var result = await new AcceptCustomBookingQuoteCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway,
                new TestCustomBookingQuoteEmailSender())
            .Handle(new AcceptCustomBookingQuoteCommand(request.Id), CancellationToken.None);

        result.Quote!.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        result.Quote.DepositPaymentCheckoutUrl.ShouldBe("https://payos.test/recovered-checkout");
        var quote = context.CustomBookingQuotes.Single();
        quote.DepositPaymentOrderCode.ShouldNotBeNull();
        quote.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        quote.DepositPaymentFailureReason.ShouldBeNull();
        paymentGateway.QueriedPaymentOrderCodes.ShouldBe([quote.DepositPaymentOrderCode.Value]);
    }

    [Test]
    public async Task AcceptQuoteRecoveryConfirmsPaidPaymentWithPromotionAndEmail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            DiscountCode = "WELCOME10",
            DiscountAmount = 500000m,
            ValidUntil = now.AddHours(1)
        };
        context.Add(request);
        context.Add(new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome 10",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10m,
            ValidFrom = now.AddDays(-1),
            ValidTo = now.AddDays(1),
            Status = "Active"
        });
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway
        {
            ThrowOnCreatePayment = true,
            RecoveredPaymentStatus = "PAID"
        };
        var quoteEmailSender = new TestCustomBookingQuoteEmailSender();

        var result = await new AcceptCustomBookingQuoteCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway,
                quoteEmailSender)
            .Handle(new AcceptCustomBookingQuoteCommand(request.Id), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Confirmed);
        result.Quote!.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Paid);
        result.Quote.DepositPaymentPaidAt.ShouldBe(now);
        context.Set<Promotion>().Single(x => x.PromotionCode == "WELCOME10").UsageCount.ShouldBe(1);
        quoteEmailSender.SentRequestIds.ShouldBe([request.Id]);
    }

    [Test]
    public async Task AcceptQuoteCanApplyCustomerDiscountCodeBeforeCreatingPaymentLink()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            ValidUntil = now.AddHours(1)
        };
        context.Add(request);
        context.Add(new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome 10",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10m,
            ValidFrom = now.AddDays(-1),
            ValidTo = now.AddDays(1),
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var paymentGateway = new TestCustomBookingPaymentGateway();
        var result = await new AcceptCustomBookingQuoteCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway,
                new TestCustomBookingQuoteEmailSender())
            .Handle(
                new AcceptCustomBookingQuoteCommand(
                    request.Id,
                    CustomBookingQuotePaymentOption.Deposit,
                    "welcome10"),
                CancellationToken.None);

        result.Quote!.DiscountCode.ShouldBe("WELCOME10");
        result.Quote.DiscountAmount.ShouldBe(500000m);
        result.Quote.QuotedPrice.ShouldBe(4500000m);
        result.Quote.DepositAmount.ShouldBe(2250000m);
        result.Quote.RemainingAmount.ShouldBe(2250000m);
        result.Quote.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        paymentGateway.CreatedDepositPayment.ShouldNotBeNull();
        paymentGateway.CreatedDepositPayment.Amount.ShouldBe(2250000);
    }

    [Test]
    public async Task DepositWebhookConfirmsBookingOnlyAfterValidPaidAmount()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Quoted;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Pending,
            DepositPaymentOrderCode = 123456,
            DiscountCode = "WELCOME10",
            DiscountAmount = 500000m
        };
        context.Add(request);
        context.Add(new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome 10",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10m,
            ValidFrom = new DateTimeOffset(2026, 6, 17, 1, 0, 0, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(2026, 6, 19, 1, 0, 0, TimeSpan.Zero),
            Status = "Active"
        });
        await context.SaveChangesAsync();

        var quoteEmailSender = new TestCustomBookingQuoteEmailSender();
        var result = await new HandleCustomBookingDepositPaymentWebhookCommandHandler(
                context,
                new TestCustomBookingPaymentGateway(),
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero)),
                quoteEmailSender)
            .Handle(
                new HandleCustomBookingDepositPaymentWebhookCommand(PaidWebhook(123456, 2500000)),
                CancellationToken.None);

        result.Processed.ShouldBeTrue();
        result.Status.ShouldBe(CustomBookingDepositPaymentStatus.Paid);
        var storedRequest = context.CustomBookingRequests.Include(x => x.Quote).Single(x => x.Id == request.Id);
        storedRequest.Status.ShouldBe(CustomBookingRequestStatus.Confirmed);
        storedRequest.QuoteAcceptedAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero));
        storedRequest.Quote!.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Paid);
        storedRequest.Quote.DepositPaymentPaidAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero));
        context.Set<Promotion>().Single(x => x.PromotionCode == "WELCOME10").UsageCount.ShouldBe(1);
        quoteEmailSender.SentRequestIds.ShouldBe([request.Id]);
    }

    [Test]
    public async Task PayOsSyncConfirmsDepositPaymentAfterReturnUrl()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Pending,
            DepositPaymentOrderCode = 123456,
            DiscountCode = "WELCOME10",
            DiscountAmount = 500000m
        };
        context.Add(request);
        context.Add(new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome 10",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10m,
            ValidFrom = new DateTimeOffset(2026, 6, 17, 1, 0, 0, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(2026, 6, 19, 1, 0, 0, TimeSpan.Zero),
            Status = "Active"
        });
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();
        paymentGateway.PaymentStatuses[123456] = new CustomBookingPaymentStatusResult(
            123456,
            2500000,
            "PAID",
            "payos-link-id");

        var quoteEmailSender = new TestCustomBookingQuoteEmailSender();
        var result = await new SyncCustomBookingPayOsPaymentCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero)),
                paymentGateway,
                quoteEmailSender)
            .Handle(new SyncCustomBookingPayOsPaymentCommand(123456), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Confirmed);
        result.Quote!.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Paid);
        var storedRequest = context.CustomBookingRequests.Include(x => x.Quote).Single(x => x.Id == request.Id);
        storedRequest.QuoteAcceptedAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero));
        storedRequest.Quote!.DepositPaymentPaidAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero));
        context.Set<Promotion>().Single(x => x.PromotionCode == "WELCOME10").UsageCount.ShouldBe(1);
        quoteEmailSender.SentRequestIds.ShouldBe([request.Id]);
    }

    [Test]
    public async Task PayOsSyncMarksCancelledDepositPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 5000000m,
            DepositPercent = 50m,
            DepositAmount = 2500000m,
            RemainingAmount = 2500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Pending,
            DepositPaymentOrderCode = 123456
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();
        paymentGateway.PaymentStatuses[123456] = new CustomBookingPaymentStatusResult(
            123456,
            2500000,
            "CANCELLED",
            "payos-link-id",
            "https://payos.test/cancelled");

        var result = await new SyncCustomBookingPayOsPaymentCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway,
                new TestCustomBookingQuoteEmailSender())
            .Handle(new SyncCustomBookingPayOsPaymentCommand(123456), CancellationToken.None);

        result.Quote!.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Cancelled);
        result.Quote.DepositPaymentCheckoutUrl.ShouldBe("https://payos.test/cancelled");
        var quote = context.CustomBookingQuotes.Single();
        quote.DepositPaymentCancelledAt.ShouldBe(now);
        quote.DepositPaymentFailureReason.ShouldBeNull();
    }

    [Test]
    public async Task PayOsSyncMarksExpiredRemainingPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero),
            RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Pending,
            RemainingPaymentOrderCode = 987654
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();
        paymentGateway.PaymentStatuses[987654] = new CustomBookingPaymentStatusResult(
            987654,
            500000,
            "EXPIRED",
            "payos-link-id",
            "https://payos.test/expired");

        var result = await new SyncCustomBookingPayOsPaymentCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero)),
                paymentGateway,
                new TestCustomBookingQuoteEmailSender())
            .Handle(new SyncCustomBookingPayOsPaymentCommand(987654), CancellationToken.None);

        result.Quote!.RemainingPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Expired);
        result.Quote.RemainingPaymentCheckoutUrl.ShouldBe("https://payos.test/expired");
        context.CustomBookingQuotes.Single().RemainingPaymentFailureReason.ShouldBeNull();
    }

    [Test]
    public async Task CustomerCanCreateRemainingPaymentAfterDepositIsPaid()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero)
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();

        var result = await new CreateCustomBookingRemainingPaymentCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero)),
                paymentGateway)
            .Handle(new CreateCustomBookingRemainingPaymentCommand(request.Id), CancellationToken.None);

        result.Quote!.RemainingPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        result.Quote.RemainingPaymentCheckoutUrl.ShouldBe("https://payos.test/checkout");
        paymentGateway.CreatedDepositPayment.ShouldNotBeNull();
        paymentGateway.CreatedDepositPayment.Amount.ShouldBe(500000);
        paymentGateway.CreatedDepositPayment.Description.ShouldBe($"260620{request.Id.ToString("N")[^3..].ToUpperInvariant()}");
        paymentGateway.CreatedDepositPayment.Description.Length.ShouldBe(9);
        paymentGateway.CreatedDepositPayment.ItemName.ShouldBe(
            $"Balance booking CB-20260620-{request.Id.ToString("N")[^6..].ToUpperInvariant()}");
    }

    [Test]
    public async Task RemainingPaymentRecoversPayOsLinkWhenCreateCallFailsAfterRemoteCreation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero)
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway
        {
            ThrowOnCreatePayment = true
        };

        var result = await new CreateCustomBookingRemainingPaymentCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero)),
                paymentGateway)
            .Handle(new CreateCustomBookingRemainingPaymentCommand(request.Id), CancellationToken.None);

        result.Quote!.RemainingPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        result.Quote.RemainingPaymentCheckoutUrl.ShouldBe("https://payos.test/recovered-checkout");
        var quote = context.CustomBookingQuotes.Single();
        quote.RemainingPaymentOrderCode.ShouldNotBeNull();
        quote.RemainingPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Pending);
        quote.RemainingPaymentFailureReason.ShouldBeNull();
        paymentGateway.QueriedPaymentOrderCodes.ShouldBe([quote.RemainingPaymentOrderCode.Value]);
    }

    [Test]
    public async Task RemainingPaymentRecoveryMarksCancelledRemotePayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var now = new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.AssignedVessel = ValidVessel();
        request.AssignedVesselId = request.AssignedVessel.Id;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero)
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway
        {
            ThrowOnCreatePayment = true,
            RecoveredPaymentStatus = "CANCELLED"
        };

        var result = await new CreateCustomBookingRemainingPaymentCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(now),
                paymentGateway)
            .Handle(new CreateCustomBookingRemainingPaymentCommand(request.Id), CancellationToken.None);

        result.Quote!.RemainingPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Cancelled);
        var quote = context.CustomBookingQuotes.Single();
        quote.RemainingPaymentCancelledAt.ShouldBe(now);
        quote.RemainingPaymentFailureReason.ShouldBeNull();
    }

    [Test]
    public async Task RemainingPaymentWebhookMarksBookingFullyPaid()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero),
            RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Pending,
            RemainingPaymentOrderCode = 987654
        };
        context.Add(request);
        await context.SaveChangesAsync();

        var result = await new HandleCustomBookingDepositPaymentWebhookCommandHandler(
                context,
                new TestCustomBookingPaymentGateway(),
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero)),
                new TestCustomBookingQuoteEmailSender())
            .Handle(
                new HandleCustomBookingDepositPaymentWebhookCommand(PaidWebhook(987654, 500000)),
                CancellationToken.None);

        result.Processed.ShouldBeTrue();
        result.Status.ShouldBe(CustomBookingDepositPaymentStatus.Paid);
        var storedRequest = context.CustomBookingRequests.Include(x => x.Quote).Single(x => x.Id == request.Id);
        storedRequest.Status.ShouldBe(CustomBookingRequestStatus.Confirmed);
        storedRequest.Quote!.RemainingPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Paid);
        storedRequest.Quote.RemainingPaymentPaidAt.ShouldBe(new DateTimeOffset(2026, 6, 18, 2, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task PassengerManifestRequiresRemainingPaymentWhenAmountIsDue()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = DateTimeOffset.UtcNow
        };
        request.AdultCount = 1;
        request.ChildCount = 0;
        request.PassengerCount = 1;
        context.Add(request);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new UpdateCustomBookingPassengerManifestCommandHandler(
                    context,
                    customerContext,
                    TimeProvider.System,
                    new TestCustomBookingConfirmationEmailSender())
                .Handle(
                    new UpdateCustomBookingPassengerManifestCommand(
                        request.Id,
                        [new CustomBookingPassengerInput("Nguyen Van A", new DateOnly(1995, 6, 20))]),
                    CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .ShouldContain("Vui lòng thanh toán đầy đủ trước khi cập nhật danh sách hành khách và nhận QR.");
        context.CustomBookingTickets.Count().ShouldBe(0);
    }

    [Test]
    public void RefundPolicyReturnsExpectedPercentByDepartureWindow()
    {
        var request = ValidRequest();
        request.DepartureDate = new DateOnly(2026, 6, 20);
        request.PreferredStartTime = new TimeOnly(8, 0);

        CustomBookingRefundPolicy.Calculate(
                request,
                1000000m,
                new DateTimeOffset(2026, 6, 16, 0, 59, 0, TimeSpan.Zero))
            .Percent.ShouldBe(100m);
        CustomBookingRefundPolicy.Calculate(
                request,
                1000000m,
                new DateTimeOffset(2026, 6, 18, 1, 0, 0, TimeSpan.Zero))
            .Percent.ShouldBe(30m);
        CustomBookingRefundPolicy.Calculate(
                request,
                1000000m,
                new DateTimeOffset(2026, 6, 19, 1, 1, 0, TimeSpan.Zero))
            .Percent.ShouldBe(0m);
    }

    [Test]
    public async Task CancelPaidBookingBeforeThreeDaysCreatesFullRefundPayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.DepartureDate = new DateOnly(2026, 6, 20);
        request.PreferredStartTime = new TimeOnly(8, 0);
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DiscountCode = "WELCOME10",
            DiscountAmount = 100000m,
            DepositPercent = 100m,
            DepositAmount = 1000000m,
            RemainingAmount = 0m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero)
        };
        context.Add(request);
        context.Add(new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome 10",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10m,
            UsageCount = 1,
            ValidFrom = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            ValidTo = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
            Status = "Active"
        });
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();

        var result = await new CancelCustomBookingRequestCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero)),
                paymentGateway)
            .Handle(
                new CancelCustomBookingRequestCommand(
                    request.Id,
                    "Khach huy",
                    "970415",
                    "123456789",
                    "NGUYEN VAN A"),
                CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Cancelled);
        paymentGateway.CreatedRefundPayout.ShouldNotBeNull();
        paymentGateway.CreatedRefundPayout.Amount.ShouldBe(1000000);
        var quote = context.CustomBookingQuotes.Single();
        quote.RefundEligiblePercent.ShouldBe(100m);
        quote.RefundAmount.ShouldBe(1000000m);
        quote.RefundStatus.ShouldBe("Created");
        quote.RefundReferenceId.ShouldNotBeNullOrWhiteSpace();
        context.Set<Promotion>().Single(x => x.PromotionCode == "WELCOME10").UsageCount.ShouldBe(0);
    }

    [Test]
    public async Task CancelPendingPaymentCancelsPayOsLinkBeforeSavingCancelledState()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Quoted;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Pending,
            DepositPaymentOrderCode = 123456
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();

        var result = await new CancelCustomBookingRequestCommandHandler(
                context,
                customerContext,
                TimeProvider.System,
                paymentGateway)
            .Handle(new CancelCustomBookingRequestCommand(request.Id, "Khach huy"), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Cancelled);
        paymentGateway.CancelledOrderCodes.ShouldBe([123456]);
        var quote = context.CustomBookingQuotes.Single();
        quote.DepositPaymentStatus.ShouldBe(CustomBookingDepositPaymentStatus.Cancelled);
        quote.DepositPaymentCancelledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task RetryFailedRefundCreatesNewPayOsPayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Cancelled;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 100m,
            DepositAmount = 1000000m,
            RemainingAmount = 0m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero),
            RefundEligiblePercent = 100m,
            RefundAmount = 1000000m,
            RefundBankBin = "string",
            RefundAccountNumber = "123456789",
            RefundAccountName = "NGUYEN VAN A",
            RefundReferenceId = "CBR-FAILED",
            RefundStatus = "Failed",
            RefundFailureReason = "Không tạo được lệnh hoàn tiền PayOS."
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();

        var result = await new RetryCustomBookingRefundCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero)),
                paymentGateway)
            .Handle(
                new RetryCustomBookingRefundCommand(
                    request.Id,
                    "970415",
                    "123456789",
                    "NGUYEN VAN A"),
                CancellationToken.None);

        result.Quote!.RefundStatus.ShouldBe("Created");
        paymentGateway.CreatedRefundPayout.ShouldNotBeNull();
        paymentGateway.CreatedRefundPayout.ToBin.ShouldBe("970415");
        paymentGateway.CreatedRefundPayout.Amount.ShouldBe(1000000);
        var quote = context.CustomBookingQuotes.Single();
        quote.RefundBankBin.ShouldBe("970415");
        quote.RefundReferenceId.ShouldNotBe("CBR-FAILED");
        quote.RefundFailureReason.ShouldBeNull();
    }

    [Test]
    public async Task SyncRefundUpdatesFailedStatusFromExistingPayOsPayout()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Cancelled;
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 100m,
            DepositAmount = 1000000m,
            RemainingAmount = 0m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero),
            RefundEligiblePercent = 100m,
            RefundAmount = 1000000m,
            RefundBankBin = "970416",
            RefundAccountNumber = "123456789",
            RefundAccountName = "NGUYEN VAN A",
            RefundReferenceId = "CBR-PAID",
            RefundStatus = "Failed",
            RefundFailureReason = "Không tạo được lệnh hoàn tiền PayOS."
        };
        context.Add(request);
        await context.SaveChangesAsync();
        var paymentGateway = new TestCustomBookingPaymentGateway();
        paymentGateway.RefundPayoutsByReferenceId["CBR-PAID"] =
            new CustomBookingRefundPayoutResult("payout-id", "SUCCEEDED", "success", "CBR-PAID", 1000000);

        var result = await new SyncCustomBookingRefundCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 19, 15, 30, 0, TimeSpan.Zero)),
                paymentGateway)
            .Handle(new SyncCustomBookingRefundCommand(request.Id), CancellationToken.None);

        result.Quote!.RefundStatus.ShouldBe("SUCCEEDED");
        result.Quote.RefundFailureReason.ShouldBeNull();
        result.Quote.RefundPayoutId.ShouldBe("payout-id");
        paymentGateway.QueriedRefundReferenceId.ShouldBe("CBR-PAID");
        var quote = context.CustomBookingQuotes.Single();
        quote.RefundStatus.ShouldBe("SUCCEEDED");
        quote.RefundFailureReason.ShouldBeNull();
        quote.RefundProcessedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task EnsureActiveTicketStoresQrExpiryAsUtc()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var request = ValidRequest();
        request.EstimatedEndDate = new DateOnly(2026, 6, 20);
        request.PreferredEndTime = new TimeOnly(10, 30);
        var issuedAt = new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

        var result = await CustomBookingTicketSupport.EnsureActiveTicketAsync(
            context,
            request,
            issuedAt,
            CancellationToken.None);

        var expectedExpiry = new DateTimeOffset(2026, 6, 20, 10, 30, 0, TimeSpan.FromHours(7))
            .ToUniversalTime();
        result.Ticket.QrExpiresAt.ShouldBe(expectedExpiry);
        result.Ticket.QrExpiresAt!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public async Task EnsureActiveTicketReturnsStoredQrTokenForExistingActiveTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var qrToken = "stored-token";
        var request = ValidRequest();
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-TEST",
            QrToken = qrToken,
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken),
            QrIssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var result = await CustomBookingTicketSupport.EnsureActiveTicketAsync(
            context,
            request,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        result.Ticket.Id.ShouldBe(ticket.Id);
        result.QrToken.ShouldBe(qrToken);
        context.CustomBookingTickets.Count().ShouldBe(1);
    }

    [Test]
    public async Task AssignedManagerCanReissueQrForUnusedActiveTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var oldQrToken = "old-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AssignedManagerUserId = managerContext.UserId;
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-TEST",
            QrToken = oldQrToken,
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(oldQrToken),
            QrIssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            QrExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var result = await new ReissueCustomBookingTicketCommandHandler(
                context,
                managerContext,
                TimeProvider.System)
            .Handle(new ReissueCustomBookingTicketCommand(request.Id, "QR không quét được"), CancellationToken.None);

        result.QrToken.ShouldNotBe(oldQrToken);
        result.QrPayload.ShouldBe(CustomBookingTicketSupport.CreateQrPayload(result.QrToken));
        var storedTicket = context.CustomBookingTickets.Single();
        storedTicket.QrToken.ShouldBe(result.QrToken);
        storedTicket.QrTokenHash.ShouldBe(CustomBookingTicketSupport.HashQrToken(result.QrToken));
        context.AuditLogs.Count().ShouldBe(1);
        context.AuditLogs.Single().Action.ShouldBe("CustomBookingTicketQrReissued");
    }

    [Test]
    public async Task AssignedStaffCanViewTicketMetadataButQrPayloadIsHidden()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var qrToken = "stored-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AssignedManagerUserId = managerContext.UserId;
        request.StaffAssignments.Add(new CustomBookingStaffAssignment
        {
            CustomBookingRequestId = request.Id,
            StaffUserId = staffContext.UserId!.Value,
            AssignedByManagerUserId = managerContext.UserId!.Value,
            AssignedAt = DateTimeOffset.UtcNow
        });
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-TEST",
            QrToken = qrToken,
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken),
            QrIssuedAt = DateTimeOffset.UtcNow,
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var result = await new GetCustomBookingTicketQueryHandler(context, staffContext)
            .Handle(new GetCustomBookingTicketQuery(request.Id), CancellationToken.None);

        result.TicketCode.ShouldBe("CBT-TEST");
        result.QrPayload.ShouldBeNull();
    }

    [Test]
    public async Task AssignedStaffCannotReissueQr()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var oldQrToken = "old-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AssignedManagerUserId = managerContext.UserId;
        request.StaffAssignments.Add(new CustomBookingStaffAssignment
        {
            CustomBookingRequestId = request.Id,
            StaffUserId = staffContext.UserId!.Value,
            AssignedByManagerUserId = managerContext.UserId!.Value,
            AssignedAt = DateTimeOffset.UtcNow
        });
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-TEST",
            QrToken = oldQrToken,
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(oldQrToken),
            QrIssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            QrExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var handler = new ReissueCustomBookingTicketCommandHandler(
            context,
            staffContext,
            TimeProvider.System);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(new ReissueCustomBookingTicketCommand(request.Id, "QR không quét được"), CancellationToken.None));

        var storedTicket = context.CustomBookingTickets.Single();
        storedTicket.QrToken.ShouldBe(oldQrToken);
        storedTicket.QrTokenHash.ShouldBe(CustomBookingTicketSupport.HashQrToken(oldQrToken));
        context.AuditLogs.Count().ShouldBe(0);
    }

    [Test]
    public async Task CustomerCanUpdatePassengerManifestAfterConfirmed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 1;
        request.PassengerCount = 2;
        context.Add(request);
        await context.SaveChangesAsync();

        var confirmationEmailSender = new TestCustomBookingConfirmationEmailSender();
        var result = await new UpdateCustomBookingPassengerManifestCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero)),
                confirmationEmailSender)
            .Handle(
                new UpdateCustomBookingPassengerManifestCommand(
                    request.Id,
                    [
                        new CustomBookingPassengerInput(
                            "Nguyen Van A",
                            new DateOnly(1995, 6, 20)),
                        new CustomBookingPassengerInput(
                            "Nguyen Van B",
                            new DateOnly(2015, 6, 21))
                    ]),
                CancellationToken.None);

        result.Status.ShouldBe(PassengerManifestStatus.Completed);
        result.PassengerCount.ShouldBe(2);
        result.AdultCount.ShouldBe(1);
        result.ChildCount.ShouldBe(1);
        result.Passengers.Single(x => x.PassengerType == CustomBookingPassengerType.Child)
            .AgeOnDepartureDate.ShouldBe(10);
        context.CustomBookingPassengers.Count().ShouldBe(2);
        context.CustomBookingRequests.Single().PassengerManifestCompletedAt.ShouldNotBeNull();
        confirmationEmailSender.SentRequestId.ShouldBe(request.Id);
        context.CustomBookingTickets.Count().ShouldBe(1);
        var ticketDto = await new GetCustomBookingTicketQueryHandler(context, customerContext)
            .Handle(new GetCustomBookingTicketQuery(request.Id), CancellationToken.None);
        ticketDto.QrPayload.ShouldStartWith("swb:custom-booking:");
    }

    [Test]
    public async Task PassengerManifestUsesDateOfBirthToCalculatePassengerType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 0;
        request.ChildCount = 1;
        request.PassengerCount = 1;
        context.Add(request);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomBookingPassengerManifestCommandHandler(
            context,
            customerContext,
            TimeProvider.System,
            new TestCustomBookingConfirmationEmailSender());

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCustomBookingPassengerManifestCommand(
                    request.Id,
                    [
                        new CustomBookingPassengerInput(
                            "Nguyen Van B",
                            new DateOnly(2015, 6, 20))
                    ]),
                CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .ShouldContain("Danh sách phải có đúng 0 người lớn.");
        exception.Errors.Values.SelectMany(x => x)
            .ShouldContain("Danh sách phải có đúng 1 trẻ em.");
        context.CustomBookingPassengers.Count().ShouldBe(0);
        context.CustomBookingRequests.Single().PassengerManifestStatus.ShouldBe(PassengerManifestStatus.NotStarted);
    }

    [Test]
    public async Task PassengerManifestImportPreviewCalculatesStatsWithoutSaving()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 1;
        request.PassengerCount = 2;
        context.Add(request);
        await context.SaveChangesAsync();

        const string csv =
            """
            FullName,DateOfBirth,PhoneNumber
            Nguyen Van A,20/06/1995,0900000000
            Nguyen Van B,21/06/2015,
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var rows = CustomBookingPassengerManifestFileParser.Parse("passengers.csv", stream);

        var result = await new PreviewCustomBookingPassengerManifestImportCommandHandler(context, customerContext)
            .Handle(new PreviewCustomBookingPassengerManifestImportCommand(request.Id, rows), CancellationToken.None);

        result.CanConfirm.ShouldBeTrue();
        result.PassengerCount.ShouldBe(2);
        result.AdultCount.ShouldBe(1);
        result.ChildCount.ShouldBe(1);
        result.Rows.Single(x => x.FullName == "Nguyen Van B").PassengerType.ShouldBe(CustomBookingPassengerType.Child);
        context.CustomBookingPassengers.Count().ShouldBe(0);
        context.CustomBookingRequests.Single().PassengerManifestStatus.ShouldBe(PassengerManifestStatus.NotStarted);
    }

    [Test]
    public void PassengerManifestDtosExposeOnlyPassengerIdentityAndAgeFields()
    {
        typeof(CustomBookingPassengerPreviewRowDto)
            .GetProperties()
            .Select(x => x.Name)
            .ShouldBe(
            [
                nameof(CustomBookingPassengerPreviewRowDto.RowNumber),
                nameof(CustomBookingPassengerPreviewRowDto.FullName),
                nameof(CustomBookingPassengerPreviewRowDto.DateOfBirth),
                nameof(CustomBookingPassengerPreviewRowDto.AgeOnDepartureDate),
                nameof(CustomBookingPassengerPreviewRowDto.PassengerType)
            ]);

        typeof(CustomBookingPassengerDto)
            .GetProperties()
            .Select(x => x.Name)
            .ShouldBe(
            [
                nameof(CustomBookingPassengerDto.Id),
                nameof(CustomBookingPassengerDto.PassengerOrder),
                nameof(CustomBookingPassengerDto.FullName),
                nameof(CustomBookingPassengerDto.PassengerType),
                nameof(CustomBookingPassengerDto.DateOfBirth),
                nameof(CustomBookingPassengerDto.AgeOnDepartureDate)
            ]);
    }

    [Test]
    public async Task PassengerManifestImportPreviewAcceptsXlsxWithTitleAndVietnameseHeaders()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 2;
        request.ChildCount = 0;
        request.PassengerCount = 2;
        context.Add(request);
        await context.SaveChangesAsync();

        using var stream = CreateVietnamesePassengerManifestXlsx();
        var rows = CustomBookingPassengerManifestFileParser.Parse("passengers.xlsx", stream);

        var result = await new PreviewCustomBookingPassengerManifestImportCommandHandler(context, customerContext)
            .Handle(new PreviewCustomBookingPassengerManifestImportCommand(request.Id, rows), CancellationToken.None);

        result.CanConfirm.ShouldBeTrue();
        result.PassengerCount.ShouldBe(2);
        result.AdultCount.ShouldBe(2);
        result.ChildCount.ShouldBe(0);
        result.Rows.Select(x => x.RowNumber).ShouldBe([4, 5]);
        result.Rows.Select(x => x.FullName).ShouldBe(["Nguyen Van An", "Tran Thi Binh"]);
        result.Rows.All(x => x.PassengerType == CustomBookingPassengerType.Adult).ShouldBeTrue();
        context.CustomBookingPassengers.Count().ShouldBe(0);
    }

    [Test]
    public async Task PassengerManifestImportPreviewAndUpdateAcceptSevenPassengerExcelTemplate()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 7;
        request.ChildCount = 0;
        request.PassengerCount = 7;
        context.Add(request);
        await context.SaveChangesAsync();

        using var stream = CreateSevenPassengerExcelTemplate();
        var rows = CustomBookingPassengerManifestFileParser.Parse("danh-sach-7-nguoi.xlsx", stream);

        var preview = await new PreviewCustomBookingPassengerManifestImportCommandHandler(context, customerContext)
            .Handle(new PreviewCustomBookingPassengerManifestImportCommand(request.Id, rows), CancellationToken.None);

        preview.CanConfirm.ShouldBeTrue();
        preview.Rows.Select(x => x.RowNumber).ShouldBe([4, 5, 6, 7, 8, 9, 10]);
        preview.Rows.Select(x => x.FullName).ShouldBe(
        [
            "Nguyễn Văn An",
            "Trần Thị Bình",
            "Lê Hoàng Cường",
            "Phạm Minh Duy",
            "Võ Ngọc Hân",
            "Đặng Gia Khánh",
            "Bùi Thanh Mai"
        ]);
        preview.Rows.Single(x => x.FullName == "Nguyễn Văn An").DateOfBirth.ShouldBe(new DateOnly(2000, 3, 12));
        preview.Rows.All(x => x.PassengerType == CustomBookingPassengerType.Adult).ShouldBeTrue();

        var passengers = preview.Rows
            .Select(x => new CustomBookingPassengerInput(
                x.FullName,
                x.DateOfBirth!.Value))
            .ToArray();

        var updated = await new UpdateCustomBookingPassengerManifestCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero)),
                new TestCustomBookingConfirmationEmailSender())
            .Handle(
                new UpdateCustomBookingPassengerManifestCommand(request.Id, passengers),
                CancellationToken.None);

        updated.Status.ShouldBe(PassengerManifestStatus.Completed);
        updated.PassengerCount.ShouldBe(7);
        updated.AdultCount.ShouldBe(7);
        updated.ChildCount.ShouldBe(0);
        context.CustomBookingPassengers.Count().ShouldBe(7);
        context.CustomBookingPassengers.Single(x => x.FullName == "Nguyễn Văn An").DateOfBirth.ShouldBe(new DateOnly(2000, 3, 12));
        context.CustomBookingRequests.Single().PassengerManifestCompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task PassengerManifestImportPreviewAcceptsMinimalXlsxWithNameAndFullBirthDateHeaders()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 1;
        request.PassengerCount = 2;
        context.Add(request);
        await context.SaveChangesAsync();

        using var stream = CreateMinimalPassengerManifestXlsx();
        var rows = CustomBookingPassengerManifestFileParser.Parse("passengers.xlsx", stream);

        var result = await new PreviewCustomBookingPassengerManifestImportCommandHandler(context, customerContext)
            .Handle(new PreviewCustomBookingPassengerManifestImportCommand(request.Id, rows), CancellationToken.None);

        result.CanConfirm.ShouldBeTrue();
        result.PassengerCount.ShouldBe(2);
        result.AdultCount.ShouldBe(1);
        result.ChildCount.ShouldBe(1);
        result.Rows.Select(x => x.RowNumber).ShouldBe([2, 3]);
        result.Rows.Select(x => x.FullName).ShouldBe(["Nguyen Van A", "Nguyen Van B"]);
        result.Rows.Single(x => x.FullName == "Nguyen Van A").DateOfBirth.ShouldBe(new DateOnly(1995, 6, 20));
        result.Rows.Single(x => x.FullName == "Nguyen Van B").PassengerType.ShouldBe(CustomBookingPassengerType.Child);
        context.CustomBookingPassengers.Count().ShouldBe(0);
    }

    [Test]
    public async Task PassengerManifestImportPreviewReportsErrorsWithoutSaving()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 1;
        request.PassengerCount = 2;
        context.Add(request);
        await context.SaveChangesAsync();

        const string csv =
            """
            FullName;PassengerType;DateOfBirth;PhoneNumber
            Nguyen Van A;Adult;20/06/1995;0900000000
            ;Child;21/06/2015;
            Nguyen Van C;Child;not-a-date;
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var rows = CustomBookingPassengerManifestFileParser.Parse("passengers.csv", stream);

        var result = await new PreviewCustomBookingPassengerManifestImportCommandHandler(context, customerContext)
            .Handle(new PreviewCustomBookingPassengerManifestImportCommand(request.Id, rows), CancellationToken.None);

        result.CanConfirm.ShouldBeFalse();
        result.PassengerCount.ShouldBe(1);
        result.Errors.ShouldContain("Danh sách hợp lệ phải có đúng 2 hành khách.");
        result.Errors.ShouldContain("File còn dòng lỗi, vui lòng sửa trước khi xác nhận.");
        result.Rows.Single(x => x.RowNumber == 3).FullName.ShouldBeNull();
        result.Rows.Single(x => x.RowNumber == 4).DateOfBirth.ShouldBeNull();
        context.CustomBookingPassengers.Count().ShouldBe(0);
        context.CustomBookingRequests.Single().PassengerManifestStatus.ShouldBe(PassengerManifestStatus.NotStarted);
    }

    [Test]
    public async Task PassengerManifestImportPreviewWarnsWhenFileTypeDiffersFromDateOfBirth()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 0;
        request.PassengerCount = 1;
        context.Add(request);
        await context.SaveChangesAsync();

        const string csv =
            """
            FullName,PassengerType,DateOfBirth
            Nguyen Van A,Child,20/06/1995
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var rows = CustomBookingPassengerManifestFileParser.Parse("passengers.csv", stream);

        var result = await new PreviewCustomBookingPassengerManifestImportCommandHandler(context, customerContext)
            .Handle(new PreviewCustomBookingPassengerManifestImportCommand(request.Id, rows), CancellationToken.None);

        result.CanConfirm.ShouldBeTrue();
        result.Warnings.ShouldContain(
            "Một số PassengerType trong file khác với kết quả hệ thống tự tính theo ngày sinh.");
        result.AdultCount.ShouldBe(1);
        result.ChildCount.ShouldBe(0);
        context.CustomBookingPassengers.Count().ShouldBe(0);
    }

    [Test]
    public async Task PassengerManifestUpdateReplacesExistingPassengerList()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 1;
        request.PassengerCount = 2;
        request.PassengerManifestStatus = PassengerManifestStatus.Completed;
        request.Passengers.Add(new CustomBookingPassenger
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            PassengerOrder = 1,
            FullName = "Old Passenger",
            PassengerType = CustomBookingPassengerType.Adult,
            DateOfBirth = new DateOnly(1990, 1, 1)
        });
        context.Add(request);
        await context.SaveChangesAsync();

        var result = await new UpdateCustomBookingPassengerManifestCommandHandler(
                context,
                customerContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero)),
                new TestCustomBookingConfirmationEmailSender())
            .Handle(
                new UpdateCustomBookingPassengerManifestCommand(
                    request.Id,
                    [
                        new CustomBookingPassengerInput(
                            "New Adult",
                            new DateOnly(1995, 6, 20)),
                        new CustomBookingPassengerInput(
                            "New Child",
                            new DateOnly(2015, 6, 21))
                    ]),
                CancellationToken.None);

        result.PassengerCount.ShouldBe(2);
        result.Passengers.Select(x => x.FullName).ShouldBe(["New Adult", "New Child"]);
        result.Passengers.Select(x => x.PassengerOrder).ShouldBe([1, 2]);
        context.CustomBookingPassengers.Count().ShouldBe(2);
        context.CustomBookingPassengers.Select(x => x.FullName).ShouldNotContain("Old Passenger");
    }

    [Test]
    public async Task AssignedStaffCanViewPassengerManifestButCannotUpdateIt()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 0;
        request.PassengerCount = 1;
        request.AssignedManagerUserId = managerContext.UserId;
        request.PassengerManifestStatus = PassengerManifestStatus.Completed;
        request.StaffAssignments.Add(new CustomBookingStaffAssignment
        {
            CustomBookingRequestId = request.Id,
            StaffUserId = staffContext.UserId!.Value,
            AssignedByManagerUserId = managerContext.UserId!.Value,
            AssignedAt = DateTimeOffset.UtcNow
        });
        request.Passengers.Add(new CustomBookingPassenger
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            PassengerOrder = 1,
            FullName = "Nguyen Van A",
            PassengerType = CustomBookingPassengerType.Adult,
            DateOfBirth = new DateOnly(1995, 6, 20)
        });
        context.Add(request);
        await context.SaveChangesAsync();

        var manifest = await new GetCustomBookingPassengerManifestQueryHandler(context, staffContext)
            .Handle(new GetCustomBookingPassengerManifestQuery(request.Id), CancellationToken.None);

        manifest.PassengerCount.ShouldBe(1);
        manifest.Passengers.Single().FullName.ShouldBe("Nguyen Van A");

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            new UpdateCustomBookingPassengerManifestCommandHandler(
                    context,
                    staffContext,
                    TimeProvider.System,
                    new TestCustomBookingConfirmationEmailSender())
                .Handle(
                    new UpdateCustomBookingPassengerManifestCommand(
                        request.Id,
                        [
                            new CustomBookingPassengerInput(
                                "Changed Name",
                                new DateOnly(1995, 6, 20))
                        ]),
                    CancellationToken.None));

        context.CustomBookingPassengers.Single().FullName.ShouldBe("Nguyen Van A");
    }

    [Test]
    public async Task PassengerManifestCannotBeUpdatedAfterCheckInLocksIt()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var qrToken = "locked-manifest-token";
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 1;
        request.ChildCount = 0;
        request.PassengerCount = 1;
        request.PassengerManifestStatus = PassengerManifestStatus.Completed;
        request.Passengers.Add(new CustomBookingPassenger
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            PassengerOrder = 1,
            FullName = "Nguyen Van A",
            PassengerType = CustomBookingPassengerType.Adult,
            DateOfBirth = new DateOnly(1995, 6, 20)
        });
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-LOCKED",
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken),
            QrIssuedAt = DateTimeOffset.UtcNow,
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        await new ScanCustomBookingTicketRequestHandler(
                context,
                staffContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 0, 45, 0, TimeSpan.Zero)))
            .Handle(
                new ScanCustomBookingTicketRequest(CustomBookingTicketSupport.CreateQrPayload(qrToken)),
                CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new UpdateCustomBookingPassengerManifestCommandHandler(
                    context,
                    customerContext,
                    TimeProvider.System,
                    new TestCustomBookingConfirmationEmailSender())
                .Handle(
                    new UpdateCustomBookingPassengerManifestCommand(
                        request.Id,
                        [
                            new CustomBookingPassengerInput(
                                "Changed Name",
                                new DateOnly(1995, 6, 20))
                        ]),
                    CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .ShouldContain("Danh sách hành khách đã khóa sau khi check-in.");
        context.CustomBookingRequests.Single().PassengerManifestStatus.ShouldBe(PassengerManifestStatus.Locked);
        context.CustomBookingPassengers.Single().FullName.ShouldBe("Nguyen Van A");
    }

    [Test]
    public async Task PassengerManifestRejectsMismatchedRegisteredCounts()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeedCustomerAsync(context);
        var request = ValidRequest();
        request.UserId = customerContext.UserId;
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.AdultCount = 2;
        request.ChildCount = 0;
        request.PassengerCount = 2;
        context.Add(request);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomBookingPassengerManifestCommandHandler(
            context,
            customerContext,
            TimeProvider.System,
            new TestCustomBookingConfirmationEmailSender());

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCustomBookingPassengerManifestCommand(
                    request.Id,
                    [
                        new CustomBookingPassengerInput(
                            "Nguyen Van A",
                            new DateOnly(1995, 6, 20))
                    ]),
                CancellationToken.None));

        exception.Errors["passengers"].ShouldContain("Danh sách phải có đúng 2 hành khách.");
    }

    [Test]
    public async Task ScanTicketMarksTicketAsUsedAndRejectsSecondScan()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var qrToken = "test-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.PassengerManifestStatus = PassengerManifestStatus.Completed;
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-TEST",
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken),
            QrIssuedAt = DateTimeOffset.UtcNow,
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var handler = new ScanCustomBookingTicketRequestHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 0, 45, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new ScanCustomBookingTicketRequest(CustomBookingTicketSupport.CreateQrPayload(qrToken)),
            CancellationToken.None);

        result.Status.ShouldBe(CustomBookingTicketStatus.Used);
        result.QrUsedAt.ShouldNotBeNull();
        context.CustomBookingTickets.Single().QrUsedByUserId.ShouldBe(staffContext.UserId);
        context.CustomBookingRequests.Single().PassengerManifestStatus.ShouldBe(PassengerManifestStatus.Locked);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new ScanCustomBookingTicketRequest(qrToken), CancellationToken.None));

        exception.Errors["qrToken"].ShouldContain("Vé này đã được sử dụng.");
    }

    [Test]
    public async Task ScanTicketBeforeCheckInWindowKeepsTicketActive()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var qrToken = "early-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        request.PassengerManifestStatus = PassengerManifestStatus.Completed;
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-EARLY",
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken),
            QrIssuedAt = DateTimeOffset.UtcNow,
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var handler = new ScanCustomBookingTicketRequestHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 0, 29, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new ScanCustomBookingTicketRequest(CustomBookingTicketSupport.CreateQrPayload(qrToken)),
                CancellationToken.None));

        exception.Errors["qrToken"].ShouldContain("Chưa đến thời gian check-in.");
        var storedTicket = context.CustomBookingTickets.Single();
        storedTicket.Status.ShouldBe(CustomBookingTicketStatus.Active);
        storedTicket.QrUsedAt.ShouldBeNull();
        storedTicket.QrUsedByUserId.ShouldBeNull();
    }

    [Test]
    public async Task ScanTicketBeforePassengerManifestCompletedKeepsTicketActive()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var qrToken = "no-manifest-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
        MarkDepositPaid(request);
        var ticket = new CustomBookingTicket
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            TicketCode = "CBT-NO-MANIFEST",
            QrTokenHash = CustomBookingTicketSupport.HashQrToken(qrToken),
            QrIssuedAt = DateTimeOffset.UtcNow,
            Status = CustomBookingTicketStatus.Active
        };
        context.AddRange(request, ticket);
        await context.SaveChangesAsync();

        var handler = new ScanCustomBookingTicketRequestHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 0, 45, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new ScanCustomBookingTicketRequest(CustomBookingTicketSupport.CreateQrPayload(qrToken)),
                CancellationToken.None));

        exception.Errors["qrToken"].ShouldContain("Danh sách hành khách chưa hoàn tất.");
        var storedTicket = context.CustomBookingTickets.Single();
        storedTicket.Status.ShouldBe(CustomBookingTicketStatus.Active);
        storedTicket.QrUsedAt.ShouldBeNull();
        context.CustomBookingRequests.Single().PassengerManifestStatus.ShouldBe(PassengerManifestStatus.NotStarted);
    }

    [Test]
    public async Task ManagerAndStaffSeeOnlyBookingsAssignedToThem()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var otherManagerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var assignedRequest = ValidRequest();
        assignedRequest.Status = CustomBookingRequestStatus.Confirmed;
        assignedRequest.AssignedManagerUserId = managerContext.UserId;
        assignedRequest.StaffAssignments.Add(new CustomBookingStaffAssignment
        {
            CustomBookingRequestId = assignedRequest.Id,
            StaffUserId = staffContext.UserId!.Value,
            AssignedByManagerUserId = managerContext.UserId!.Value,
            AssignedAt = DateTimeOffset.UtcNow
        });
        var otherRequest = ValidRequest();
        otherRequest.Status = CustomBookingRequestStatus.Confirmed;
        otherRequest.AssignedManagerUserId = otherManagerContext.UserId;
        context.AddRange(assignedRequest, otherRequest);
        await context.SaveChangesAsync();

        var managerResult = await new GetCustomBookingRequestsQueryHandler(context, managerContext)
            .Handle(new GetCustomBookingRequestsQuery(), CancellationToken.None);
        var staffResult = await new GetCustomBookingRequestsQueryHandler(context, staffContext)
            .Handle(new GetCustomBookingRequestsQuery(), CancellationToken.None);

        managerResult.Select(x => x.Id).ShouldBe([assignedRequest.Id]);
        staffResult.Select(x => x.Id).ShouldBe([assignedRequest.Id]);
    }

    [Test]
    public void OperationPlanValidatorRejectsDuplicateStaffAndInvalidServices()
    {
        var staffId = Guid.NewGuid();
        var result = new UpdateCustomBookingOperationPlanCommandValidator().Validate(
            new UpdateCustomBookingOperationPlanCommand(
                Guid.NewGuid(),
                [
                    new CustomBookingStaffPlanItem(staffId),
                    new CustomBookingStaffPlanItem(staffId)
                ],
                [new CustomBookingOperationServicePlanItem(string.Empty, 0)]));

        result.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateCustomBookingOperationPlanCommand.StaffAssignments));
        result.Errors.ShouldContain(x => x.PropertyName.Contains(nameof(CustomBookingOperationServicePlanItem.ServiceName)));
        result.Errors.ShouldContain(x => x.PropertyName.Contains(nameof(CustomBookingOperationServicePlanItem.Quantity)));
    }

    [Test]
    public void PendingReviewResponseOmitsNullAuditAndDuplicateFields()
    {
        var fromStation = new Station
        {
            StationCode = "FROM",
            StationName = "From",
            Latitude = 10m,
            Longitude = 106m
        };
        var toStation = new Station
        {
            StationCode = "TO",
            StationName = "To",
            Latitude = 10.01m,
            Longitude = 106.01m
        };
        var request = ValidRequest();
        request.FromStationId = fromStation.Id;
        request.FromStation = fromStation;
        request.FromStationCode = fromStation.StationCode;
        request.ToStationId = toStation.Id;
        request.ToStation = toStation;
        request.ToStationCode = toStation.StationCode;
        request.SpecialRequests = "KTV";

        var json = JsonSerializer.Serialize(
            CustomBookingRequestDto.From(request),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            });

        json.ShouldContain("\"specialRequests\":\"KTV\"");
        json.ShouldContain("\"status\":\"PendingReview\"");
        json.ShouldNotContain("\"createdAt\"");
        json.ShouldNotContain("\"assignedAt\"");
        json.ShouldNotContain("\"assignedByUserId\"");
        json.ShouldNotContain("\"quotedAt\"");
        json.ShouldNotContain("\"quoteAcceptedAt\"");
        json.ShouldNotContain("\"cancelledAt\"");
        json.ShouldNotContain("\"cancelledByUserId\"");
        json.ShouldNotContain("\"assignedVessel\"");
        json.ShouldNotContain("\"preferredVessel\"");
        json.ShouldNotContain("\"statusReason\"");
        json.ShouldNotContain("\"quote\"");
        json.ShouldNotContain("\"preferredEndTime\"");
        json.ShouldNotContain("\"fromLocation\"");
        json.ShouldNotContain("\"toLocation\"");
        json.ShouldNotContain("\"itineraryNote\"");
    }

    private static UpdateCustomBookingRequestCommand ValidUpdateCommand() =>
        new(
            Guid.NewGuid(),
            null,
            2,
            SeatSetupType.StandardAndVip,
            VesselRentalUnit.Day,
            new DateOnly(2026, 6, 20),
            new TimeOnly(8, 0),
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            1);

    private static CustomBookingRequest ValidRequest() =>
        new()
        {
            ContactName = "Customer",
            ContactPhone = "+84900000000",
            RequestedNumberOfDecks = 2,
            RequestedSeatSetupType = SeatSetupType.StandardAndVip,
            RentalUnit = VesselRentalUnit.Day,
            DepartureDate = new DateOnly(2026, 6, 20),
            PreferredStartTime = new TimeOnly(8, 0),
            FromLocation = "A",
            ToLocation = "B",
            PassengerCount = 20,
            AdultCount = 20,
            Status = CustomBookingRequestStatus.PendingReview,
            EstimatedDurationMinutes = 300
        };

    private static void MarkDepositPaid(CustomBookingRequest request)
    {
        request.Quote = new CustomBookingQuote
        {
            CustomBookingRequestId = request.Id,
            CustomBookingRequest = request,
            QuotedPrice = 1000000m,
            DepositPercent = 50m,
            DepositAmount = 500000m,
            RemainingAmount = 500000m,
            Currency = "VND",
            DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            DepositPaymentPaidAt = DateTimeOffset.UtcNow,
            RemainingPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
            RemainingPaymentPaidAt = DateTimeOffset.UtcNow
        };
    }

    private static CustomBookingDepositPaymentWebhook PaidWebhook(long orderCode, long amount) =>
        new(
            "00",
            "success",
            true,
            new CustomBookingDepositPaymentWebhookData(
                orderCode,
                amount,
                $"SWB{orderCode % 1_000_000:000000}",
                "12345678",
                "TF230204212323",
                "2026-06-18 08:00:00",
                "VND",
                "payos-link-id",
                "00",
                "Thanh cong",
                null,
                null,
                null,
                null,
                null,
                null),
            "valid-signature");

    private static Vessel ValidVessel(string code = "WB01", decimal price = 12000000m)
    {
        var vessel = new Vessel
        {
            Code = code,
            Name = code,
            Status = VesselStatus.Active,
            SeatCount = 30,
            NumberOfDecks = 2,
            SeatSetupType = SeatSetupType.StandardAndVip,
            SeatsConfigured = true
        };
        vessel.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = vessel.Id,
            Vessel = vessel,
            RentalUnit = VesselRentalUnit.Day,
            UnitPrice = price,
            Currency = "VND"
        });
        return vessel;
    }

    private static Station Station(string code) =>
        new()
        {
            StationCode = code,
            StationName = code,
            Status = StationStatus.Active
        };

    private static WaterbusService Service(string code, BookingMode bookingMode, int displayOrder) =>
        new()
        {
            Code = code,
            Name = code,
            BookingMode = bookingMode,
            DisplayOrder = displayOrder,
            IsActive = true
        };

    private static UserStationAssignment StationAssignment(
        Guid userId,
        Guid stationId,
        Guid assignedByUserId) =>
        new()
        {
            UserId = userId,
            StationId = stationId,
            IsPrimary = true,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = assignedByUserId
        };

    private static async Task<TestUserContext> SeedCustomerAsync(Infrastructure.Data.ApplicationDbContext context)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Custom booking customer",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return new TestUserContext(user.Id);
    }

    private static MemoryStream CreateVietnamesePassengerManifestXlsx()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sharedStrings = archive.CreateEntry("xl/sharedStrings.xml");
            using (var writer = new StreamWriter(sharedStrings.Open(), Encoding.UTF8))
            {
                writer.Write(
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <x:sst xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />
                    """);
            }

            var worksheet = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(worksheet.Open(), Encoding.UTF8))
            {
                writer.Write(
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <x:worksheet xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <x:sheetData>
                        <x:row r="1">
                          <x:c r="A1" t="str"><x:v>DANH SACH 7 NGUOI</x:v></x:c>
                        </x:row>
                        <x:row r="2" />
                        <x:row r="3">
                          <x:c r="A3" t="str"><x:v>STT</x:v></x:c>
                          <x:c r="B3" t="str"><x:v>Họ và tên</x:v></x:c>
                          <x:c r="C3" t="str"><x:v>Giới tính</x:v></x:c>
                          <x:c r="D3" t="str"><x:v>Ngày sinh</x:v></x:c>
                          <x:c r="E3" t="str"><x:v>Số điện thoại</x:v></x:c>
                          <x:c r="F3" t="str"><x:v>Email</x:v></x:c>
                          <x:c r="G3" t="str"><x:v>Địa chỉ</x:v></x:c>
                          <x:c r="H3" t="str"><x:v>Ghi chú</x:v></x:c>
                        </x:row>
                        <x:row r="4">
                          <x:c r="A4" t="n"><x:v>1</x:v></x:c>
                          <x:c r="B4" t="str"><x:v>Nguyen Van An</x:v></x:c>
                          <x:c r="C4" t="str"><x:v>Nam</x:v></x:c>
                          <x:c r="D4" t="n"><x:v>36597</x:v></x:c>
                          <x:c r="E4" t="str"><x:v>0901234567</x:v></x:c>
                          <x:c r="H4" t="str"><x:v></x:v></x:c>
                        </x:row>
                        <x:row r="5">
                          <x:c r="A5" t="n"><x:v>2</x:v></x:c>
                          <x:c r="B5" t="str"><x:v>Tran Thi Binh</x:v></x:c>
                          <x:c r="C5" t="str"><x:v>Nu</x:v></x:c>
                          <x:c r="D5" t="n"><x:v>37097</x:v></x:c>
                          <x:c r="E5" t="str"><x:v>0902345678</x:v></x:c>
                          <x:c r="H5" t="str"><x:v></x:v></x:c>
                        </x:row>
                      </x:sheetData>
                    </x:worksheet>
                    """);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateSevenPassengerExcelTemplate()
    {
        var sharedStrings = new[]
        {
            "DANH SÁCH 7 NGƯỜI",
            "STT",
            "Họ và tên",
            "Giới tính",
            "Ngày sinh",
            "Số điện thoại",
            "Email",
            "Địa chỉ",
            "Ghi chú",
            "Nguyễn Văn An",
            "Nam",
            "0901 234 567",
            "an.nguyen@example.com",
            "TP Hồ Chí Minh",
            "Trần Thị Bình",
            "Nữ",
            "0902 345 678",
            "binh.tran@example.com",
            "Hà Nội",
            "Lê Hoàng Cường",
            "0903 456 789",
            "cuong.le@example.com",
            "Đà Nẵng",
            "Phạm Minh Duy",
            "0904 567 890",
            "duy.pham@example.com",
            "Cần Thơ",
            "Võ Ngọc Hân",
            "0905 678 901",
            "han.vo@example.com",
            "Huế",
            "Đặng Gia Khánh",
            "0906 789 012",
            "khanh.dang@example.com",
            "Bình Dương",
            "Bùi Thanh Mai",
            "0907 890 123",
            "mai.bui@example.com",
            "Đồng Nai"
        };

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteSharedStrings(archive, sharedStrings);
            WriteWorkbookForFirstWorksheet(archive, "worksheets/sheet1.xml");

            var worksheet = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(worksheet.Open(), Encoding.UTF8))
            {
                writer.Write(
                    $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <x:worksheet xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <x:sheetData>
                        <x:row r="1">
                          {{Cell("A1", 0)}}
                        </x:row>
                        <x:row r="2" />
                        <x:row r="3">
                          {{Cell("A3", 1)}}
                          {{Cell("B3", 2)}}
                          {{Cell("C3", 3)}}
                          {{Cell("D3", 4)}}
                          {{Cell("E3", 5)}}
                          {{Cell("F3", 6)}}
                          {{Cell("G3", 7)}}
                          {{Cell("H3", 8)}}
                        </x:row>
                        {{PassengerRow(4, 1, 9, 10, new DateOnly(2000, 3, 12), 11, 12, 13)}}
                        {{PassengerRow(5, 2, 14, 15, new DateOnly(2001, 7, 25), 16, 17, 18)}}
                        {{PassengerRow(6, 3, 19, 10, new DateOnly(1999, 11, 4), 20, 21, 22)}}
                        {{PassengerRow(7, 4, 23, 10, new DateOnly(2002, 1, 18), 24, 25, 26)}}
                        {{PassengerRow(8, 5, 27, 15, new DateOnly(2000, 9, 30), 28, 29, 30)}}
                        {{PassengerRow(9, 6, 31, 10, new DateOnly(1998, 5, 9), 32, 33, 34)}}
                        {{PassengerRow(10, 7, 35, 15, new DateOnly(2003, 12, 22), 36, 37, 38)}}
                      </x:sheetData>
                      <x:mergeCells count="1">
                        <x:mergeCell ref="A1:H1" />
                      </x:mergeCells>
                    </x:worksheet>
                    """);
            }
        }

        stream.Position = 0;
        return stream;

        static string Cell(string reference, int sharedStringIndex) =>
            $"""<x:c r="{reference}" t="s"><x:v>{sharedStringIndex}</x:v></x:c>""";

        static string PassengerRow(
            int rowNumber,
            int order,
            int fullNameIndex,
            int genderIndex,
            DateOnly dateOfBirth,
            int phoneIndex,
            int emailIndex,
            int addressIndex) =>
            $$"""
            <x:row r="{{rowNumber}}">
              <x:c r="A{{rowNumber}}" t="n"><x:v>{{order}}</x:v></x:c>
              {{Cell($"B{rowNumber}", fullNameIndex)}}
              {{Cell($"C{rowNumber}", genderIndex)}}
              <x:c r="D{{rowNumber}}" t="n"><x:v>{{ToExcelSerialDate(dateOfBirth)}}</x:v></x:c>
              {{Cell($"E{rowNumber}", phoneIndex)}}
              {{Cell($"F{rowNumber}", emailIndex)}}
              {{Cell($"G{rowNumber}", addressIndex)}}
              <x:c r="H{{rowNumber}}" t="str"><x:v></x:v></x:c>
            </x:row>
            """;

        static int ToExcelSerialDate(DateOnly value) =>
            (int)value.ToDateTime(TimeOnly.MinValue).ToOADate();
    }

    private static void WriteSharedStrings(ZipArchive archive, IReadOnlyList<string> sharedStrings)
    {
        var sharedStringsEntry = archive.CreateEntry("xl/sharedStrings.xml");
        using var writer = new StreamWriter(sharedStringsEntry.Open(), Encoding.UTF8);
        writer.Write(
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <x:sst xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                count="{{sharedStrings.Count}}" uniqueCount="{{sharedStrings.Count}}">
            """);
        foreach (var value in sharedStrings)
        {
            writer.Write($"<x:si><x:t>{value}</x:t></x:si>");
        }

        writer.Write("</x:sst>");
    }

    private static void WriteWorkbookForFirstWorksheet(ZipArchive archive, string worksheetTarget)
    {
        var workbook = archive.CreateEntry("xl/workbook.xml");
        using (var writer = new StreamWriter(workbook.Open(), Encoding.UTF8))
        {
            writer.Write(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <x:workbook xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                    xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <x:sheets>
                    <x:sheet name="Danh sach" sheetId="1" r:id="rId1" />
                  </x:sheets>
                </x:workbook>
                """);
        }

        var relationships = archive.CreateEntry("xl/_rels/workbook.xml.rels");
        using (var writer = new StreamWriter(relationships.Open(), Encoding.UTF8))
        {
            writer.Write(
                $$"""
                <?xml version="1.0" encoding="utf-8"?>
                <x:Relationships xmlns:x="http://schemas.openxmlformats.org/package/2006/relationships">
                  <x:Relationship Id="rId1"
                      Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                      Target="{{worksheetTarget}}" />
                </x:Relationships>
                """);
        }
    }

    private static MemoryStream CreateMinimalPassengerManifestXlsx()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteWorkbookForFirstWorksheet(archive, "worksheets/sheet2.xml");

            var worksheet = archive.CreateEntry("xl/worksheets/sheet2.xml");
            using (var writer = new StreamWriter(worksheet.Open(), Encoding.UTF8))
            {
                writer.Write(
                    """
                    <?xml version="1.0" encoding="utf-8"?>
                    <x:worksheet xmlns:x="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <x:sheetData>
                        <x:row r="1">
                          <x:c r="A1" t="str"><x:v>Tên</x:v></x:c>
                          <x:c r="B1" t="str"><x:v>Ngày tháng năm sinh</x:v></x:c>
                        </x:row>
                        <x:row r="2">
                          <x:c r="A2" t="str"><x:v>Nguyen Van A</x:v></x:c>
                          <x:c r="B2" t="d"><x:v>1995-06-20T00:00:00</x:v></x:c>
                        </x:row>
                        <x:row r="3">
                          <x:c r="A3" t="str"><x:v>Nguyen Van B</x:v></x:c>
                          <x:c r="B3" t="d"><x:v>2015-06-21T00:00:00</x:v></x:c>
                        </x:row>
                      </x:sheetData>
                    </x:worksheet>
                    """);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestCustomBookingConfirmationEmailSender : ICustomBookingConfirmationEmailSender
    {
        public Guid? SentRequestId { get; private set; }

        public Task SendConfirmationAsync(
            CustomBookingRequest request,
            CancellationToken cancellationToken)
        {
            SentRequestId = request.Id;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCustomBookingQuoteEmailSender : ICustomBookingQuoteEmailSender
    {
        public List<Guid> SentRequestIds { get; } = [];

        public Task SendQuoteAsync(CustomBookingRequest request, CancellationToken cancellationToken)
        {
            SentRequestIds.Add(request.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class TestCustomBookingPaymentGateway : ICustomBookingPaymentGateway
    {
        public CustomBookingDepositPaymentRequest? CreatedDepositPayment { get; private set; }

        public CustomBookingRefundPayoutRequest? CreatedRefundPayout { get; private set; }

        public bool ThrowOnCreatePayment { get; init; }

        public string RecoveredPaymentStatus { get; init; } = "PENDING";

        public Dictionary<long, CustomBookingPaymentStatusResult> PaymentStatuses { get; } = [];

        public Dictionary<string, CustomBookingRefundPayoutResult> RefundPayoutsByReferenceId { get; } = [];

        public string? QueriedRefundReferenceId { get; private set; }

        public List<long> QueriedPaymentOrderCodes { get; } = [];

        public List<long> CancelledOrderCodes { get; } = [];

        public Task<CustomBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CustomBookingDepositPaymentRequest request,
            CancellationToken cancellationToken)
        {
            CreatedDepositPayment = request;
            if (ThrowOnCreatePayment)
            {
                PaymentStatuses[request.OrderCode] = new CustomBookingPaymentStatusResult(
                    request.OrderCode,
                    request.Amount,
                    RecoveredPaymentStatus,
                    "recovered-payos-link-id",
                    "https://payos.test/recovered-checkout");
                throw new PaymentGatewayException("Không tạo được link thanh toán PayOS.");
            }

            return Task.FromResult(new CustomBookingDepositPaymentResult(
                "payos-link-id",
                "https://payos.test/checkout",
                "payos-qr",
                "PENDING"));
        }

        public Task<CustomBookingPaymentCancellationResult> CancelPaymentAsync(
            long orderCode,
            string reason,
            CancellationToken cancellationToken)
        {
            CancelledOrderCodes.Add(orderCode);
            return Task.FromResult(new CustomBookingPaymentCancellationResult(
                orderCode.ToString(),
                "CANCELLED",
                reason));
        }

        public Task<CustomBookingPaymentStatusResult> GetPaymentAsync(
            long orderCode,
            CancellationToken cancellationToken)
        {
            QueriedPaymentOrderCodes.Add(orderCode);
            return Task.FromResult(PaymentStatuses.TryGetValue(orderCode, out var result)
                ? result
                : new CustomBookingPaymentStatusResult(orderCode, null, "PENDING", null));
        }

        public Task<CustomBookingRefundPayoutResult> CreateRefundPayoutAsync(
            CustomBookingRefundPayoutRequest request,
            CancellationToken cancellationToken)
        {
            CreatedRefundPayout = request;
            return Task.FromResult(new CustomBookingRefundPayoutResult("payout-id", "Created", "ok"));
        }

        public Task<CustomBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
            string referenceId,
            CancellationToken cancellationToken)
        {
            QueriedRefundReferenceId = referenceId;
            return Task.FromResult(RefundPayoutsByReferenceId.GetValueOrDefault(referenceId));
        }

        public bool IsValidWebhook(CustomBookingDepositPaymentWebhook webhook) => true;
    }
}
