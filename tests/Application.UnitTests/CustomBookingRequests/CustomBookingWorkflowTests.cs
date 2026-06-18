using System.Text.Json;
using System.Text.Json.Serialization;
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
    [TestCase(CustomBookingRequestStatus.Confirmed, false)]
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
    public async Task QuoteUsesSelectedRentalUnitPrice()
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
                TimeProvider.System,
                new TestCustomBookingQuoteEmailSender())
            .Handle(new QuoteCustomBookingRequestCommand(request.Id, 50, null), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Quoted);
        result.Quote.ShouldNotBeNull();
        result.Quote.QuotedPrice.ShouldBe(33333.33m);
        result.Quote.DepositAmount.ShouldBe(16667m);
        result.Quote.RemainingAmount.ShouldBe(16666.33m);
        result.Quote.PriceNote.ShouldBe("Theo gio");
    }

    [Test]
    public async Task AcceptQuoteCreatesQrTicketOnce()
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
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
        };
        context.Add(request);
        await context.SaveChangesAsync();

        var result = await new AcceptCustomBookingQuoteCommandHandler(
                context,
                customerContext,
                TimeProvider.System,
                new TestCustomBookingConfirmationEmailSender())
            .Handle(new AcceptCustomBookingQuoteCommand(request.Id), CancellationToken.None);

        result.Status.ShouldBe(CustomBookingRequestStatus.Confirmed);
        result.Ticket.ShouldNotBeNull();
        result.Ticket.QrPayload.ShouldStartWith("swb:custom-booking:");
        result.Ticket.QrToken.ShouldNotBeNullOrWhiteSpace();
        context.CustomBookingTickets.Count().ShouldBe(1);
        context.CustomBookingTickets.Single().QrTokenHash.ShouldBe(
            CustomBookingTicketSupport.HashQrToken(result.Ticket.QrToken));
        context.CustomBookingTickets.Single().QrTokenHash.ShouldNotBe(result.Ticket.QrToken);
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
    public async Task ScanTicketMarksTicketAsUsedAndRejectsSecondScan()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var qrToken = "test-token";
        var request = ValidRequest();
        request.Status = CustomBookingRequestStatus.Confirmed;
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
            TimeProvider.System);

        var result = await handler.Handle(
            new ScanCustomBookingTicketRequest(CustomBookingTicketSupport.CreateQrPayload(qrToken)),
            CancellationToken.None);

        result.Status.ShouldBe(CustomBookingTicketStatus.Used);
        result.QrUsedAt.ShouldNotBeNull();
        context.CustomBookingTickets.Single().QrUsedByUserId.ShouldBe(staffContext.UserId);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new ScanCustomBookingTicketRequest(qrToken), CancellationToken.None));

        exception.Errors["qrToken"].ShouldContain("Vé này đã được sử dụng.");
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

    private sealed class TestCustomBookingConfirmationEmailSender : ICustomBookingConfirmationEmailSender
    {
        public Task SendConfirmationAsync(
            CustomBookingRequest request,
            string? qrPayload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestCustomBookingQuoteEmailSender : ICustomBookingQuoteEmailSender
    {
        public Task SendQuoteAsync(CustomBookingRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
