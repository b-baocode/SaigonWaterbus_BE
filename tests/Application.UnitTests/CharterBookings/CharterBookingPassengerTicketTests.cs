using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingPassengerTicketTests
{
    [Test]
    public async Task UpdatingPassengersCreatesTicketForEachPassenger()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.ContactEmail = "customer@example.test";
        booking.Payments.Add(new Payment
        {
            PaymentCode = $"PAY{Guid.NewGuid():N}"[..20],
            Amount = booking.TotalAmount,
            Currency = booking.Currency,
            PaymentMethod = "Cash",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = DateTimeOffset.UtcNow
        });
        context.Add(booking);
        await context.SaveChangesAsync();

        var notificationSender = new TestPaymentNotificationSender();
        var handler = CreateUpdateHandler(context, userId, notificationSender);

        var result = await handler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        result.RegisteredPassengerCount.ShouldBe(2);
        result.CharterBookingQrToken.ShouldNotBeNullOrWhiteSpace();
        result.TicketCount.ShouldBe(2);
        result.Tickets.Count.ShouldBe(2);
        booking.CharterBookingQrToken.ShouldBe(result.CharterBookingQrToken);

        var tickets = context.Tickets.OrderBy(x => x.TicketCode).ToArray();
        tickets.Length.ShouldBe(2);
        tickets.All(x => x.BookingPassengerId.HasValue).ShouldBeTrue();
        tickets.Select(x => x.QrToken).Distinct().Count().ShouldBe(2);

        var boardingPass = notificationSender.BoardingPasses.Single();
        var attachment = boardingPass.Attachments.ShouldNotBeNull().Single();
        attachment.Name.ShouldBe($"{booking.BookingCode}-boarding-pass.pdf");
        attachment.ContentType.ShouldBe("application/pdf");
        attachment.Content.ShouldBe([1, 2, 3]);
    }

    [Test]
    public async Task UpdatingPassengersCanExceedRequestedCountUpToSelectedBoatCapacity()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var result = await handler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992),
                    new CharterBookingPassengerRequest("Le Van C", 1988)
                ]),
            CancellationToken.None);

        result.PassengerCount.ShouldBe(3);
        result.RegisteredPassengerCount.ShouldBe(3);
        result.AdultCount.ShouldBe(3);
        result.TicketCount.ShouldBe(3);
    }

    [Test]
    public async Task UpdatingPassengersRejectsPassengerCountAboveSelectedBoatCapacity()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingPassengersCommand(
                    booking.Id,
                    [
new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992),
                    new CharterBookingPassengerRequest("Le Van C", 1988),
                        new CharterBookingPassengerRequest("Pham Thi D", 1989),
                        new CharterBookingPassengerRequest("Hoang Van E", 1991)
                    ]),
                CancellationToken.None));

        exception.Errors["passengers"].Single()
            .ShouldContain("sức chứa của tàu đã chọn (4)");
    }

    [Test]
    public async Task ImportingPassengersCanExceedRequestedCountUpToSelectedBoatCapacity()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = CreateImportHandler(context, userId);
        var csv = "fullName,birthYear\nNguyen Van A,1990\nTran Thi B,1992\nLe Van C,1988\n";

        var result = await handler.Handle(
            new ImportCharterBookingPassengersCommand(
                booking.Id,
                "passengers.csv",
                System.Text.Encoding.UTF8.GetBytes(csv)),
            CancellationToken.None);

        result.PassengerCount.ShouldBe(3);
        result.RegisteredPassengerCount.ShouldBe(3);
        result.AdultCount.ShouldBe(3);
        result.TicketCount.ShouldBe(3);
    }

    [Test]
    public async Task ImportingPassengersWithInsuranceRequiresAdditionalPaymentForNewPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.ContactEmail = "customer@example.test";
        booking.InsuranceSnapshots.Add(InsuranceSnapshot(unitPremiumAmount: 10_000m, quantity: 2));
        booking.SubtotalAmount += booking.InsuranceSnapshots[0].TotalAmount;
        booking.TotalAmount += booking.InsuranceSnapshots[0].TotalAmount;
        booking.DepositAmount = booking.TotalAmount;
        booking.RemainingAmount = 0m;
        booking.Payments.Add(PaidPayment(booking.TotalAmount));
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = CreateImportHandler(context, userId, new TestPaymentNotificationSender());
        var csv = "fullName,birthYear\nNguyen Van A,1990\nTran Thi B,1992\nLe Van C,1988\n";

        var result = await handler.Handle(
            new ImportCharterBookingPassengersCommand(
                booking.Id,
                "passengers.csv",
                System.Text.Encoding.UTF8.GetBytes(csv)),
            CancellationToken.None);

        result.PassengerCount.ShouldBe(3);
        result.RegisteredPassengerCount.ShouldBe(3);
        result.PaymentStatus.ShouldBe("DepositPaid");
        result.RequiresAdditionalPayment.ShouldBeTrue();
        result.AdditionalInsuranceAmount.ShouldBe(10_000m);
        result.TotalAmount.ShouldBe(1_030_000m);
        result.DepositAmount.ShouldBe(1_020_000m);
        result.RemainingAmount.ShouldBe(10_000m);
        // Charter flow mới: gói ThirdParty được lưu trong Insurance.
        result.Insurance.ShouldNotBeNull();
        result.Insurance!.Quantity.ShouldBe(3);
        result.Insurance.TotalAmount.ShouldBe(30_000m);
        result.OptionalInsurances.ShouldBeNull();

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.InsuranceSnapshots.ShouldNotBeEmpty();
        savedBooking.InsuranceSnapshots[0].TotalAmount.ShouldBe(30_000m);
        savedBooking.RemainingAmount.ShouldBe(10_000m);
    }

    [Test]
    public async Task ApprovingPassengerAddRequestPreservesExistingTicketsAndCreatesTicketsForNewPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.ContactEmail = "customer@example.test";
        booking.Payments.Add(new Payment
        {
            PaymentCode = $"PAY{Guid.NewGuid():N}"[..20],
            Amount = booking.TotalAmount,
            Currency = booking.Currency,
            PaymentMethod = "Cash",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = DateTimeOffset.UtcNow
        });
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, userId);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);
        var originalTickets = context.Tickets
            .Where(x => x.TicketStatus == TicketStatus.Active)
            .ToDictionary(x => x.Id, x => x.QrToken);

        var addHandler = CreateAddHandler(context, userId);
        var result = await addHandler.Handle(
            new AddCharterBookingPassengersCommand(
                booking.Id,
                [new CharterBookingPassengerRequest("Le Van C", 1988)]),
            CancellationToken.None);

        result.PassengerCount.ShouldBe(2);
        result.RegisteredPassengerCount.ShouldBe(3);
        result.TicketCount.ShouldBe(2);
        context.Set<BookingPassenger>().Single(x => x.FullName == "Le Van C")
            .ApprovalStatus.ShouldBe(CharterBookingPassengerSupport.ApprovalStatusPending);
        booking.BookingStatus.ShouldBe(BookingStatus.PendingApproval);

        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var notificationSender = new TestPaymentNotificationSender();
        var approveHandler = CreateApproveHandler(context, adminContext.UserId!.Value, notificationSender);
        var requestBatchId = context.Set<BookingPassenger>()
            .Single(x => x.FullName == "Le Van C")
            .RequestBatchId
            .ShouldNotBeNull();

        var approved = await approveHandler.Handle(
            new ApproveCharterBookingPassengerAddRequestCommand(booking.Id, requestBatchId),
            CancellationToken.None);

        approved.TicketCount.ShouldBe(3);
        approved.PaymentStatus.ShouldBe("Paid");
        approved.RequiresAdditionalPayment.ShouldBeFalse();
        approved.AdditionalInsuranceAmount.ShouldBe(0m);
        approved.RemainingAmount.ShouldBe(0m);
        var activeTickets = context.Tickets
            .Where(x => x.TicketStatus == TicketStatus.Active)
            .ToArray();
        activeTickets.Length.ShouldBe(3);
        foreach (var (ticketId, qrToken) in originalTickets)
        {
            activeTickets.Single(x => x.Id == ticketId).QrToken.ShouldBe(qrToken);
        }
        context.Set<BookingPassenger>().Single(x => x.FullName == "Le Van C")
            .ApprovalStatus.ShouldBe(CharterBookingPassengerSupport.ApprovalStatusApproved);
        notificationSender.BoardingPasses.Single().Attachments.ShouldNotBeNull().Single().Content.ShouldBe([1, 2, 3]);
    }

    [Test]
    public async Task ApprovingPassengerAddRequestWithInsuranceRequiresAdditionalPayment()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.ContactEmail = "customer@example.test";
        booking.InsuranceSnapshots.Add(InsuranceSnapshot(unitPremiumAmount: 10_000m, quantity: 2));
        booking.SubtotalAmount += booking.InsuranceSnapshots[0].TotalAmount;
        booking.TotalAmount += booking.InsuranceSnapshots[0].TotalAmount;
        booking.DepositAmount = booking.TotalAmount;
        booking.RemainingAmount = 0m;
        booking.Payments.Add(PaidPayment(booking.TotalAmount));
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, userId);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var addHandler = CreateAddHandler(context, userId);
        await addHandler.Handle(
            new AddCharterBookingPassengersCommand(
                booking.Id,
                [new CharterBookingPassengerRequest("Le Van C", 1988)]),
            CancellationToken.None);

        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var notificationSender = new TestPaymentNotificationSender();
        var approveHandler = CreateApproveHandler(context, adminContext.UserId!.Value, notificationSender);
        var requestBatchId = context.Set<BookingPassenger>()
            .Single(x => x.FullName == "Le Van C")
            .RequestBatchId
            .ShouldNotBeNull();

        var result = await approveHandler.Handle(
            new ApproveCharterBookingPassengerAddRequestCommand(booking.Id, requestBatchId),
            CancellationToken.None);

        result.TicketCount.ShouldBe(3);
        result.PaymentStatus.ShouldBe("DepositPaid");
        result.RequiresAdditionalPayment.ShouldBeTrue();
        result.AdditionalInsuranceAmount.ShouldBe(10_000m);
        result.TotalAmount.ShouldBe(1_030_000m);
        result.DepositAmount.ShouldBe(1_020_000m);
        result.RemainingAmount.ShouldBe(10_000m);
        // Charter flow mới: gói ThirdParty được lưu trong Insurance, không phải OptionalInsurances.
        result.Insurance.ShouldNotBeNull();
        result.Insurance!.Quantity.ShouldBe(3);
        result.Insurance.TotalAmount.ShouldBe(30_000m);
        result.OptionalInsurances.ShouldBeNull();

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.PaymentStatus.ShouldBe("DepositPaid");
        savedBooking.BookingStatus.ShouldBe(BookingStatus.Approved);
        savedBooking.DepositAmount.ShouldBe(1_020_000m);
        savedBooking.RemainingAmount.ShouldBe(10_000m);
        savedBooking.InsuranceSnapshots.ShouldNotBeEmpty();
        savedBooking.InsuranceSnapshots[0].Quantity.ShouldBe(3);
        savedBooking.InsuranceSnapshots[0].TotalAmount.ShouldBe(30_000m);
        notificationSender.BoardingPasses.Count.ShouldBe(0);
    }

    [Test]
    public async Task AddingPassengersRejectsWithinTwentyFourHoursBeforeDeparture()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.StartTime = new TimeOnly(8, 0);
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();
        var cutoff = new DateTimeOffset(2029, 12, 31, 1, 0, 0, TimeSpan.Zero);
        var handler = CreateAddHandler(context, userId, timeProvider: new FixedTimeProvider(cutoff));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new AddCharterBookingPassengersCommand(
                    booking.Id,
                    [new CharterBookingPassengerRequest("Le Van C", 1988)]),
                CancellationToken.None));

        exception.Errors["passengers"].Single()
            .ShouldContain("48 giờ trước giờ khởi hành");
    }

    [Test]
    public async Task AddingPassengersRejectsSecondAddRequest()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = CreateAddHandler(context, userId);
        await handler.Handle(
            new AddCharterBookingPassengersCommand(
                booking.Id,
                [new CharterBookingPassengerRequest("Le Van C", 1988)]),
            CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new AddCharterBookingPassengersCommand(
                    booking.Id,
                    [new CharterBookingPassengerRequest("Pham Thi D", 1989)]),
                CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x).Single()
            .ShouldContain("đã được xác nhận");
    }

    [Test]
    public async Task AddingPassengersRequiresConfirmedBookingStatus()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.BookingStatus = BookingStatus.Approved;
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var handler = CreateAddHandler(context, userId);
        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new AddCharterBookingPassengersCommand(
                    booking.Id,
                    [new CharterBookingPassengerRequest("Le Van C", 1988)]),
                CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x).Single()
            .ShouldContain("đã được xác nhận");
    }

    [Test]
    public async Task AssignedManagerCanRejectPassengerAddRequestWithNote()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.AssignedManagerId = managerContext.UserId;
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();

        var addHandler = CreateAddHandler(context, userId);
        await addHandler.Handle(
            new AddCharterBookingPassengersCommand(
                booking.Id,
                [new CharterBookingPassengerRequest("Le Van C", 1988)]),
            CancellationToken.None);
        var requestBatchId = context.Set<BookingPassenger>()
            .Single(x => x.FullName == "Le Van C")
            .RequestBatchId
            .ShouldNotBeNull();

        var rejectHandler = CreateRejectHandler(context, managerContext.UserId!.Value);
        await rejectHandler.Handle(
            new RejectCharterBookingPassengerAddRequestCommand(
                booking.Id,
                requestBatchId,
                "Thong tin hanh khach khong hop le"),
            CancellationToken.None);

        var passenger = context.Set<BookingPassenger>().Single(x => x.FullName == "Le Van C");
        passenger.ApprovalStatus.ShouldBe(CharterBookingPassengerSupport.ApprovalStatusRejected);
        passenger.ReviewNote.ShouldBe("Thong tin hanh khach khong hop le");
        passenger.ReviewedByUserId.ShouldBe(managerContext.UserId);
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
    }

    [Test]
    public async Task UpdatingPassengersRejectsWithinTwentyFourHoursBeforeDeparture()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(userId, adultCount: 2);
        booking.StartTime = new TimeOnly(8, 0);
        AttachSelectedBoat(booking, boat);
        context.AddRange(boat, booking);
        await context.SaveChangesAsync();
        var withinCutoff = new DateTimeOffset(2029, 12, 31, 1, 30, 0, TimeSpan.Zero);
        var handler = CreateUpdateHandler(context, userId, timeProvider: new FixedTimeProvider(withinCutoff));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingPassengersCommand(
                    booking.Id,
                    [
                        new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                        new CharterBookingPassengerRequest("Tran Thi B", 1992)
                    ]),
                CancellationToken.None));

        exception.Errors["passengers"].Single()
            .ShouldContain("48 giờ trước giờ khởi hành");
    }

    [Test]
    public async Task ReplacingPassengersCancelsOldTicketsAndCreatesNewTickets()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCharterBooking(userId, adultCount: 2);
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);
        await handler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var firstTicketIds = context.Tickets.Select(x => x.Id).ToHashSet();

        await handler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Le Van C", 1988),
                    new CharterBookingPassengerRequest("Pham Thi D", 1989)
                ]),
            CancellationToken.None);

        var tickets = context.Tickets.ToArray();
        tickets.Length.ShouldBe(4);
        tickets.Count(x => firstTicketIds.Contains(x.Id) && x.TicketStatus == TicketStatus.Cancelled)
            .ShouldBe(2);

        var activeTickets = tickets.Where(x => x.TicketStatus == TicketStatus.Active).ToArray();
        activeTickets.Length.ShouldBe(2);
        activeTickets.All(x => x.BookingPassengerId.HasValue).ShouldBeTrue();

        var currentPassengerIds = context.Set<BookingPassenger>().Select(x => x.Id).ToHashSet();
        activeTickets.Select(x => x.BookingPassengerId!.Value).ToHashSet()
            .SetEquals(currentPassengerIds)
            .ShouldBeTrue();
    }

    [Test]
    public async Task UpdatingSinglePassengerBookingAllowsMissingDateOfBirthWhenPassengerTypeCanBeInferred()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCharterBooking(userId, adultCount: 1);
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var result = await handler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [new CharterBookingPassengerRequest("TRUMP", null)]),
            CancellationToken.None);

        result.RegisteredPassengerCount.ShouldBe(1);
        result.TicketCount.ShouldBe(1);
        result.Passengers.Single().FullName.ShouldBe("TRUMP");
        result.Passengers.Single().PassengerType.ShouldBe(CharterBookingPassengerType.Adult.ToString());
    }

    [Test]
    public async Task UpdatingPassengersAcceptsBirthYearOnly()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var currentYear = DateTime.UtcNow.Year;
        var adultBirthYear = currentYear - 30;
        var childBirthYear = currentYear - 5;
        var booking = PaidCharterBooking(userId, adultCount: 1, childCount: 1);
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var result = await handler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", adultBirthYear),
                    new CharterBookingPassengerRequest("Tran Thi B", childBirthYear)
                ]),
            CancellationToken.None);

        result.RegisteredPassengerCount.ShouldBe(2);
        result.AdultCount.ShouldBe(1);
        result.ChildCount.ShouldBe(1);
        result.Passengers.Select(x => x.BirthYear).Order().ShouldBe([adultBirthYear, childBirthYear]);

        var savedPassengers = context.Set<BookingPassenger>().ToArray();
        savedPassengers.Select(x => x.BirthYear).Order().ShouldBe([adultBirthYear, childBirthYear]);
    }

    [Test]
    public void PassengerManifestParserAcceptsBirthYearColumn()
    {
        var csv = "fullName,birthYear\nNguyen Van A,1990\nTran Thi B,2021\n";

        var passengers = PassengerManifestParser.Parse(
            "passengers.csv",
            System.Text.Encoding.UTF8.GetBytes(csv),
            new DateOnly(2026, 1, 1));

        passengers.Count.ShouldBe(2);
        passengers.Select(x => x.BirthYear).ShouldBe([1990, 2021]);
    }

    [Test]
    public async Task UpdatingSinglePassengerBookingWithEmptyPassengerListThrowsValidation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCharterBooking(userId, adultCount: 1);
        booking.ContactName = "TRUMP";
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingPassengersCommand(booking.Id, []),
                CancellationToken.None));

        exception.Errors.Values.ShouldContain(messages => messages.Contains(
            "Booking phải có ít nhất 1 hành khách (người lớn hoặc trẻ em)."));
    }

    [Test]
    public async Task UpdatingPassengersReturnsSpecificDateOfBirthErrorWhenPassengerTypeCannotBeInferred()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = PaidCharterBooking(userId, adultCount: 1, childCount: 1);
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = CreateUpdateHandler(context, userId);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingPassengersCommand(
                    booking.Id,
                    [new CharterBookingPassengerRequest("TRUMP", null)]),
                CancellationToken.None));

        exception.Errors["passengers[0].birthYear"].Single()
            .ShouldBe("birthYear is required.");
    }

    [Test]
    public async Task ExportTicketsReturnsPassengerTicketsForBookingOwner()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCharterBooking(user.Id, adultCount: 2);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var exportHandler = new ExportCharterBookingTicketsQueryHandler(
            context,
            new TestUserContext(user.Id));

        var export = await exportHandler.Handle(
            new ExportCharterBookingTicketsQuery(booking.Id),
            CancellationToken.None);

        export.BookingCode.ShouldBe(booking.BookingCode);
        export.Tickets.Count.ShouldBe(2);
        export.Tickets.ShouldAllBe(x => x.PassengerId.HasValue);
        export.Tickets.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.QrToken));
    }

    [Test]
    public async Task ExportTicketsCanSelectSpecificTicketIds()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCharterBooking(user.Id, adultCount: 2);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var selectedTicketId = context.Tickets
            .OrderBy(x => x.TicketCode)
            .Select(x => x.Id)
            .First();
        var exportHandler = new ExportCharterBookingTicketsQueryHandler(
            context,
            new TestUserContext(user.Id));

        var export = await exportHandler.Handle(
            new ExportCharterBookingTicketsQuery(booking.Id, [selectedTicketId]),
            CancellationToken.None);

        export.Tickets.Count.ShouldBe(1);
        export.Tickets.Single().TicketId.ShouldBe(selectedTicketId);
    }

    [Test]
    public async Task ExportTicketsByQrTokenReturnsTicketsForEmailPdfLink()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCharterBooking(user.Id, adultCount: 2);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var qrToken = context.Tickets
            .OrderBy(x => x.TicketCode)
            .Select(x => x.QrToken)
            .First();
        var exportHandler = new ExportCharterBookingTicketsByQrTokenQueryHandler(context);

        var export = await exportHandler.Handle(
            new ExportCharterBookingTicketsByQrTokenQuery(qrToken),
            CancellationToken.None);

        export.BookingCode.ShouldBe(booking.BookingCode);
        export.Tickets.Count.ShouldBe(2);
        export.Tickets.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.QrToken));
    }

    [Test]
    public async Task ExportTicketsRejectsInvalidTicketIds()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var booking = PaidCharterBooking(user.Id, adultCount: 1);
        context.AddRange(user.Role, user, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [new CharterBookingPassengerRequest("Nguyen Van A", 1990)]),
            CancellationToken.None);

        var exportHandler = new ExportCharterBookingTicketsQueryHandler(
            context,
            new TestUserContext(user.Id));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            exportHandler.Handle(
                new ExportCharterBookingTicketsQuery(booking.Id, [Guid.NewGuid()]),
                CancellationToken.None));

        exception.Errors["ticketIds"].Single()
            .ShouldContain("không thuộc charter booking");
    }

    [Test]
    public async Task ManifestCanBeFoundByBookingCode()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var station = new Station
        {
            StationCode = "BD",
            StationName = "Ben Bach Dang"
        };
        var booking = PaidCharterBooking(user.Id, adultCount: 2);
        booking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = booking,
            Station = station,
            StationId = station.Id,
            StopOrder = 1,
            StayDurationMinutes = 30,
            Note = "Don khach"
        });
        context.AddRange(user.Role, user, station, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var manifestHandler = new GetCharterBookingManifestByCodeQueryHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var manifest = await manifestHandler.Handle(
            new GetCharterBookingManifestByCodeQuery(booking.BookingCode.ToLowerInvariant()),
            CancellationToken.None);

        manifest.BookingId.ShouldBe(booking.Id);
        manifest.BookingCode.ShouldBe(booking.BookingCode);
        manifest.RegisteredPassengerCount.ShouldBe(2);
        manifest.TicketSummary.TotalTickets.ShouldBe(2);
        manifest.TicketSummary.ActiveTickets.ShouldBe(2);
        manifest.ItineraryStops.Single().StationName.ShouldBe("Ben Bach Dang");
        manifest.Passengers.Count.ShouldBe(2);
        manifest.Passengers.ShouldAllBe(x => x.TicketId.HasValue);
        manifest.Passengers.ShouldAllBe(x => x.CanCheckIn);
    }

    [Test]
    public async Task StaffCanCheckInAndCheckOutAllTicketsByCharterBookingQrToken()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var user = Customer();
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var booking = PaidCharterBooking(user.Id, adultCount: 2);
        booking.BoatId = boat.Id;
        booking.Boat = boat;
        context.AddRange(user.Role, user, boat, booking);
        await context.SaveChangesAsync();

        var updateHandler = CreateUpdateHandler(context, user.Id);
        await updateHandler.Handle(
            new UpdateCharterBookingPassengersCommand(
                booking.Id,
                [
                    new CharterBookingPassengerRequest("Nguyen Van A", 1990),
                    new CharterBookingPassengerRequest("Tran Thi B", 1992)
                ]),
            CancellationToken.None);

        var qrToken = booking.CharterBookingQrToken.ShouldNotBeNull();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var staffUser = context.Users.Single(x => x.Id == staffContext.UserId!.Value);
        context.StaffWorkAssignments.Add(new StaffWorkAssignment
        {
            StaffUserId = staffUser.Id,
            StaffUser = staffUser,
            AssignmentType = StaffWorkAssignmentType.Boat,
            BoatId = boat.Id,
            Boat = boat,
            WorkingDate = booking.DepartureDate!.Value,
            StartAt = new DateTimeOffset(booking.DepartureDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)),
            EndAt = new DateTimeOffset(booking.DepartureDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)),
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUser.Id,
            AssignedByUser = staffUser,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        var checkedInAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var checkInHandler = new UpdateCharterBookingAttendanceCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(checkedInAt));

        var checkInResult = await checkInHandler.Handle(
            new UpdateCharterBookingAttendanceCommand(
                qrToken,
                CharterBookingAttendanceAction.CheckIn,
                CharterBookingAttendanceMode.All,
                TicketIds: null),
            CancellationToken.None);

        checkInResult.UpdatedCount.ShouldBe(2);
        checkInResult.SkippedCount.ShouldBe(0);
        checkInResult.Manifest.TicketSummary.CheckedInTickets.ShouldBe(2);
        context.Tickets.ShouldAllBe(x => x.TicketStatus == TicketStatus.CheckedIn);
        context.Tickets.ShouldAllBe(x => x.CheckedInAt == checkedInAt);
        context.Tickets.ShouldAllBe(x => x.CheckedInByUserId == staffContext.UserId);

        var checkedOutAt = new DateTimeOffset(2030, 1, 1, 16, 0, 0, TimeSpan.Zero);
        var checkOutHandler = new UpdateCharterBookingAttendanceCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(checkedOutAt));

        var checkOutResult = await checkOutHandler.Handle(
            new UpdateCharterBookingAttendanceCommand(
                qrToken,
                CharterBookingAttendanceAction.CheckOut,
                CharterBookingAttendanceMode.All,
                TicketIds: null),
            CancellationToken.None);

        checkOutResult.UpdatedCount.ShouldBe(2);
        checkOutResult.SkippedCount.ShouldBe(0);
        checkOutResult.Manifest.TicketSummary.CheckedOutTickets.ShouldBe(2);
        context.Tickets.ShouldAllBe(x => x.TicketStatus == TicketStatus.CheckedOut);
        context.Tickets.ShouldAllBe(x => x.CheckedOutAt == checkedOutAt);
        context.Tickets.ShouldAllBe(x => x.CheckedOutByUserId == staffContext.UserId);
        context.TicketScanEvents.Count(x => x.Action == TicketScanAction.CheckIn).ShouldBe(2);
        context.TicketScanEvents.Count(x => x.Action == TicketScanAction.CheckOut).ShouldBe(2);
        context.TicketScanEvents.ShouldAllBe(x => x.Result == TicketScanResult.Success);
        context.TicketScanEvents.Select(x => x.TicketId).Distinct().Count().ShouldBe(2);
        booking.BookingStatus.ShouldBe(BookingStatus.Completed);
    }

    private static UpdateCharterBookingPassengersCommandHandler CreateUpdateHandler(
        IApplicationDbContext context,
        Guid userId,
        TestPaymentNotificationSender? paymentNotificationSender = null,
        TimeProvider? timeProvider = null) =>
        new(
            context,
            new TestUserContext(userId),
            paymentNotificationSender ?? new TestPaymentNotificationSender(),
            new TestCharterBookingTicketPdfRenderer(),
            timeProvider ?? TimeProvider.System);

    private static AddCharterBookingPassengersCommandHandler CreateAddHandler(
        IApplicationDbContext context,
        Guid userId,
        TimeProvider? timeProvider = null) =>
        new(
            context,
            new TestUserContext(userId),
            timeProvider ?? TimeProvider.System);

    private static ApproveCharterBookingPassengerAddRequestCommandHandler CreateApproveHandler(
        IApplicationDbContext context,
        Guid userId,
        TestPaymentNotificationSender? paymentNotificationSender = null,
        TimeProvider? timeProvider = null) =>
        new(
            context,
            new TestUserContext(userId),
            paymentNotificationSender ?? new TestPaymentNotificationSender(),
            new TestCharterBookingTicketPdfRenderer(),
            timeProvider ?? TimeProvider.System);

    private static RejectCharterBookingPassengerAddRequestCommandHandler CreateRejectHandler(
        IApplicationDbContext context,
        Guid userId,
        TimeProvider? timeProvider = null) =>
        new(
            context,
            new TestUserContext(userId),
            timeProvider ?? TimeProvider.System);

    private static ImportCharterBookingPassengersCommandHandler CreateImportHandler(
        IApplicationDbContext context,
        Guid userId,
        TestPaymentNotificationSender? paymentNotificationSender = null,
        TimeProvider? timeProvider = null) =>
        new(
            context,
            new TestUserContext(userId),
            paymentNotificationSender ?? new TestPaymentNotificationSender(),
            new TestCharterBookingTicketPdfRenderer(),
            timeProvider ?? TimeProvider.System);

    private static Booking PaidCharterBooking(Guid userId, int adultCount, int childCount = 0) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = adultCount,
            ChildCount = childCount,
            PassengerCount = adultCount + childCount,
            SubtotalAmount = 1_000_000,
            TotalAmount = 1_000_000,
            DepositAmount = 1_000_000,
            RemainingAmount = 0
        };

    private static void AttachSelectedBoat(Booking booking, Boat boat)
    {
        booking.BoatId = boat.Id;
        booking.Boat = boat;
        booking.CharterBoats.Add(new CharterBookingBoat
        {
            BookingId = booking.Id,
            Booking = booking,
            BoatId = boat.Id,
            Boat = boat,
            BoatOrder = 1,
            SeatSetupType = boat.SeatSetupType,
            UnitPrice = 1_000_000,
            ChargeableDurationValue = 1,
            SubtotalAmount = 1_000_000
        });
    }

    private static Payment PaidPayment(decimal amount) =>
        new()
        {
            PaymentCode = $"PAY{Guid.NewGuid():N}"[..20],
            Provider = "PayOS",
            Amount = amount,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Paid",
            PaidAt = DateTimeOffset.UtcNow
        };

    private static BookingInsuranceSnapshot InsuranceSnapshot(decimal unitPremiumAmount, int quantity) =>
        new()
        {
            InsurancePackageId = Guid.NewGuid(),
            Code = "INS-CHARTER",
            Name = "Charter insurance",
            BookingType = Booking.CharterBookingType,
            UnitPremiumAmount = unitPremiumAmount,
            Currency = "VND",
            Quantity = quantity,
            TotalAmount = unitPremiumAmount * quantity,
            QuotedAt = DateTimeOffset.UtcNow
        };

    private static User Customer()
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };

        return new User
        {
            FullName = "Customer",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };
    }

    private sealed class TestPaymentNotificationSender : IPaymentNotificationSender
    {
        public List<BoardingPassNotification> BoardingPasses { get; } = [];

        public Task SendPaymentSucceededAsync(
            PaymentSucceededNotification notification,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendBoardingPassAsync(
            BoardingPassNotification notification,
            CancellationToken cancellationToken)
        {
            BoardingPasses.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendRefundReleasedAsync(
            RefundReleasedNotification notification,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendCharterETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestCharterBookingTicketPdfRenderer : ICharterBookingTicketPdfRenderer
    {
        public byte[] Render(CharterBookingTicketExportDto export) => [1, 2, 3];
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
