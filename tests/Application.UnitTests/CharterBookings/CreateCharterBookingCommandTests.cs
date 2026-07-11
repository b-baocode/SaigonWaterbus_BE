using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CreateCharterBookingCommandTests
{
    [Test]
    public async Task CreateStoresRequestedBoatsAndReturnsThemInDetail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-CREATE", "Bến đi");
        context.AddRange(role, user, fromStation);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB0001"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new CreateCharterBookingCommand(
                new DateOnly(2026, 7, 20),
                BoatRentalUnit.Day,
                1,
                AdultCount: 10,
                ChildCount: 2,
                FromStationId: fromStation.Id,
                RequestedBoats:
                [
                    new CreateCharterBookingBoatRequest(1),
                    new CreateCharterBookingBoatRequest(2)
                ],
                ContactName: "Nguyen Van B",
                ContactPhone: "0911111111",
                ContactEmail: "booking-contact@example.test"),
            CancellationToken.None);

        result.RequestedBoatCount.ShouldBe(2);
        result.RequestedBoats.Select(x => x.NumberOfDecks)
            .ShouldBe([1, 2]);

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.RequestedBoatCount.ShouldBe(2);
        booking.RequestedBoatDecks.ShouldBe("1,2");
        booking.RequestedBoatTypes.ShouldBeNull();
        booking.PreferredSeatSetupType.ShouldBeNull();
        booking.PromotionId.ShouldBeNull();
        booking.ContactName.ShouldBe("Nguyen Van B");
        booking.ContactPhone.ShouldBe("0911111111");
        booking.ContactEmail.ShouldBe("booking-contact@example.test");

        var detail = await new GetCharterBookingDetailQueryHandler(
                context,
                new TestUserContext(user.Id))
            .Handle(new GetCharterBookingDetailQuery(result.BookingId), CancellationToken.None);

        detail.RequestedBoatCount.ShouldBe(2);
        detail.RequestedBoats.Select(x => x.NumberOfDecks)
            .ShouldBe([1, 2]);
        detail.ContactName.ShouldBe("Nguyen Van B");
        detail.ContactPhone.ShouldBe("0911111111");
        detail.ContactEmail.ShouldBe("booking-contact@example.test");
    }

    [Test]
    public async Task CreateRequiresPhoneAfterAccountFallback()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-PHONE", "Bến đi");
        context.AddRange(role, user, fromStation);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB0002"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateCharterBookingCommand(
                    new DateOnly(2026, 7, 20),
                    BoatRentalUnit.Day,
                    1,
                    AdultCount: 10,
                    ChildCount: 2,
                    FromStationId: fromStation.Id,
                    ContactEmail: "customer@example.test"),
                CancellationToken.None));

        exception.Errors["contactPhone"].Single()
            .ShouldContain("Số điện thoại");
    }

    [Test]
    public async Task CreateStoresSelectedInsuranceAndReturnsItInDetail()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-INS", "Bến đi");
        var insurancePackage = CharterInsurancePackage(unitPremiumAmount: 10_000m);
        context.AddRange(role, user, fromStation, insurancePackage);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB-INS-001"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new CreateCharterBookingCommand(
                new DateOnly(2026, 7, 20),
                BoatRentalUnit.Day,
                1,
                AdultCount: 10,
                ChildCount: 2,
                FromStationId: fromStation.Id,
                InsuranceSelected: true,
                InsurancePackageId: insurancePackage.Id),
            CancellationToken.None);

        var booking = context.Set<Booking>().Single(x => x.Id == result.BookingId);
        booking.InsuranceSnapshot.ShouldNotBeNull();
        booking.InsuranceSnapshot.InsurancePackageId.ShouldBe(insurancePackage.Id);
        booking.InsuranceSnapshot.Quantity.ShouldBe(12);
        booking.InsuranceSnapshot.TotalAmount.ShouldBe(120_000m);

        var detail = await new GetCharterBookingDetailQueryHandler(
                context,
                new TestUserContext(user.Id))
            .Handle(new GetCharterBookingDetailQuery(result.BookingId), CancellationToken.None);

        detail.InsuranceSelected.ShouldBeTrue();
        detail.InsurancePackageId.ShouldBe(insurancePackage.Id);
        detail.Insurance.ShouldNotBeNull();
        detail.Insurance.Selected.ShouldBeTrue();
        detail.Insurance.Quantity.ShouldBe(12);
        detail.Insurance.TotalAmount.ShouldBe(120_000m);
    }

    [Test]
    public async Task CreateRejectsDuplicateActiveRequestWithExistingBookingCode()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-DUP-FROM", "Bến đi");
        var toStation = WaterbusStation("ST-DUP-TO", "Bến đến");
        var stopStation = WaterbusStation("ST-DUP-STOP", "Bến dừng");
        var existingBooking = DuplicateCharterBooking(
            user,
            fromStation,
            toStation,
            "CB-DUP-001",
            BookingStatus.PendingQuote);
        existingBooking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = existingBooking,
            BookingId = existingBooking.Id,
            Station = stopStation,
            StationId = stopStation.Id,
            StopOrder = 1,
            StayDurationMinutes = 20,
            Note = "Old note"
        });
        context.AddRange(role, user, fromStation, toStation, stopStation, existingBooking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB-DUP-NEW"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                DuplicateCreateCommand(
                    fromStation.Id,
                    toStation.Id,
                    stopStation.Id,
                    contactPhone: "0900 000 000",
                    contactEmail: "CUSTOMER@example.test"),
                CancellationToken.None));

        exception.Errors["duplicateBooking"].Single()
            .ShouldContain("CB-DUP-001");
        context.Set<Booking>().Count().ShouldBe(1);
    }

    [Test]
    public async Task CreateAllowsDuplicateWhenExistingRequestIsCancelled()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-CANCEL-FROM", "Bến đi");
        var toStation = WaterbusStation("ST-CANCEL-TO", "Bến đến");
        var stopStation = WaterbusStation("ST-CANCEL-STOP", "Bến dừng");
        var existingBooking = DuplicateCharterBooking(
            user,
            fromStation,
            toStation,
            "CB-CANCELLED-001",
            BookingStatus.Cancelled);
        existingBooking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = existingBooking,
            BookingId = existingBooking.Id,
            Station = stopStation,
            StationId = stopStation.Id,
            StopOrder = 1,
            StayDurationMinutes = 20
        });
        context.AddRange(role, user, fromStation, toStation, stopStation, existingBooking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB-ALLOWED-NEW"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            DuplicateCreateCommand(fromStation.Id, toStation.Id, stopStation.Id),
            CancellationToken.None);

        result.BookingCode.ShouldBe("CB-ALLOWED-NEW");
        context.Set<Booking>().Count().ShouldBe(2);
    }

    [Test]
    public async Task ListReturnsCustomerSummaryFieldsAndCharterBookingCodePrefix()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = new Station
        {
            StationCode = "ST-A",
            StationName = "Bến A",
            Status = StationStatus.Active
        };
        var toStation = new Station
        {
            StationCode = "ST-B",
            StationName = "Bến B",
            Status = StationStatus.Active
        };
        context.AddRange(role, user, fromStation, toStation);
        await context.SaveChangesAsync();

        var createHandler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("BK-20260705-ABCDE"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var result = await createHandler.Handle(
            new CreateCharterBookingCommand(
                new DateOnly(2026, 7, 20),
                BoatRentalUnit.Day,
                1,
                AdultCount: 10,
                ChildCount: 2,
                StartTime: new TimeOnly(8, 4),
                FromStationId: fromStation.Id,
                ToStationId: toStation.Id,
                RequestedBoats:
                [
                    new CreateCharterBookingBoatRequest(1)
                ]),
            CancellationToken.None);

        result.BookingCode.ShouldBe("CB-20260705-ABCDE");

        var list = await new GetCharterBookingListQueryHandler(
                context,
                new TestUserContext(user.Id))
            .Handle(new GetCharterBookingListQuery(), CancellationToken.None);

        var item = list.Single();
        item.Id.ShouldBe(result.BookingId);
        item.BookingCode.ShouldBe("CB-20260705-ABCDE");
        item.BookingStatus.ShouldBe("PendingQuote");
        item.PaymentStatus.ShouldBe("Unpaid");
        item.DepartureDate.ShouldBe("2026-07-20");
        item.StartTime.ShouldBe("08:04:00");
        item.RentalUnit.ShouldBe("Day");
        item.DurationValue.ShouldBe(1);
        item.AdultCount.ShouldBe(10);
        item.ChildCount.ShouldBe(2);
        item.PassengerCount.ShouldBe(12);
        item.FromStationName.ShouldBe("Bến A");
        item.ToStationName.ShouldBe("Bến B");
        item.BoatName.ShouldBeNull();
        item.SubtotalAmount.ShouldBeNull();
        item.FinalAmount.ShouldBeNull();
        item.RequestedBoats.Select(x => x.NumberOfDecks).ShouldBe([1]);
    }

    [Test]
    public async Task UpdateStoresContactInfoAndDoesNotRevalidateUnchangedDepartureDate()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "account@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-EDIT", "Bến đi");
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-EDIT-001",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = "receive@example.test",
            FromStation = fromStation,
            FromStationId = fromStation.Id,
            DepartureDate = new DateOnly(2026, 7, 10),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            ChildCount = 1,
            PassengerCount = 2,
            RequestedBoatCount = 1,
            RequestedBoatDecks = "1",
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };
        context.AddRange(role, user, fromStation, booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var detail = await handler.Handle(
            new UpdateCharterBookingCommand(
                booking.Id,
                new DateOnly(2026, 7, 10),
                BoatRentalUnit.Day,
                2,
                AdultCount: 2,
                ChildCount: 1,
                StartTime: new TimeOnly(9, 30),
                RequestedBoats: [new CreateCharterBookingBoatRequest(2)],
                ContactName: "Updated customer",
                ContactPhone: "0988888888",
                ContactEmail: "updated@example.test"),
            CancellationToken.None);

        detail.ContactName.ShouldBe("Updated customer");
        detail.ContactPhone.ShouldBe("0988888888");
        detail.ContactEmail.ShouldBe("updated@example.test");
        detail.DepartureDate.ShouldBe(new DateOnly(2026, 7, 10));
        detail.StartTime.ShouldBe(new TimeOnly(9, 30));
        detail.DurationValue.ShouldBe(2);
        detail.PassengerCount.ShouldBe(3);

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.ContactName.ShouldBe("Updated customer");
        savedBooking.ContactPhone.ShouldBe("0988888888");
        savedBooking.ContactEmail.ShouldBe("updated@example.test");
        savedBooking.RequestedBoatDecks.ShouldBe("2");
        savedBooking.RequestedBoatTypes.ShouldBeNull();
    }

    [Test]
    public async Task CreateRejectsExternalDepartureStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var externalStation = WaterbusStation("EXT", "Bến ngoài");
        externalStation.IsWaterbusStation = false;
        context.AddRange(role, user, externalStation);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedBookingCodeGenerator("CB0003"),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new CreateCharterBookingCommand(
                    new DateOnly(2026, 7, 20),
                    BoatRentalUnit.Day,
                    1,
                    AdultCount: 10,
                    ChildCount: 2,
                    FromStationId: externalStation.Id),
                CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .Single()
            .ShouldContain("Waterbus");
    }

    [Test]
    public async Task UpdateRejectsExternalDepartureStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "account@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var validStation = WaterbusStation("ST-VALID", "Bến Waterbus");
        var externalStation = WaterbusStation("ST-EXT", "Bến ngoài");
        externalStation.IsWaterbusStation = false;
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-EDIT-EXT",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = "receive@example.test",
            FromStation = validStation,
            FromStationId = validStation.Id,
            DepartureDate = new DateOnly(2026, 7, 10),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            ChildCount = 0,
            PassengerCount = 1,
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };
        context.AddRange(role, user, validStation, externalStation, booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingCommand(
                    booking.Id,
                    FromStationId: externalStation.Id),
                CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x)
            .Single()
            .ShouldContain("Waterbus");
    }

    [Test]
    public async Task UpdatePreservesExistingValuesWhenFieldsAreNotSubmitted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "account@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = new Station
        {
            StationCode = "ST-FROM",
            StationName = "Bến đi",
            Status = StationStatus.Active
        };
        var toStation = new Station
        {
            StationCode = "ST-TO",
            StationName = "Bến đến",
            Status = StationStatus.Active
        };
        var stopStation = new Station
        {
            StationCode = "ST-STOP",
            StationName = "Bến dừng",
            Status = StationStatus.Active
        };
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-EDIT-KEEP",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = "receive@example.test",
            FromStation = fromStation,
            FromStationId = fromStation.Id,
            ToStation = toStation,
            ToStationId = toStation.Id,
            DepartureDate = new DateOnly(2026, 7, 10),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            ChildCount = 0,
            PassengerCount = 1,
            RequestedBoatCount = 1,
            RequestedBoatDecks = "1",
            BoatRequirements = "Old boat note",
            SpecialRequests = "Old special note",
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };
        booking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = booking,
            BookingId = booking.Id,
            Station = stopStation,
            StationId = stopStation.Id,
            StopOrder = 1,
            StayDurationMinutes = 15,
            Note = "Old stop note"
        });
        context.AddRange(role, user, fromStation, toStation, stopStation, booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var detail = await handler.Handle(
            new UpdateCharterBookingCommand(
                booking.Id,
                SpecialRequests: "New special note"),
            CancellationToken.None);

        detail.DepartureDate.ShouldBe(new DateOnly(2026, 7, 10));
        detail.StartTime.ShouldBe(new TimeOnly(8, 0));
        detail.FromStationId.ShouldBe(fromStation.Id);
        detail.ToStationId.ShouldBe(toStation.Id);
        detail.DurationValue.ShouldBe(1);
        detail.PassengerCount.ShouldBe(1);
        detail.RequestedBoatCount.ShouldBe(1);
        detail.RequestedBoats.Single().NumberOfDecks.ShouldBe(1);
        detail.SpecialRequests.ShouldBe("New special note");
        detail.ItineraryStops.Single().StationId.ShouldBe(stopStation.Id);
        detail.ItineraryStops.Single().StayDurationMinutes.ShouldBe(15);
        detail.ItineraryStops.Single().Note.ShouldBe("Old stop note");

        var savedBooking = context.Set<Booking>().Include(x => x.ItineraryStops).Single(x => x.Id == booking.Id);
        savedBooking.RequestedBoatDecks.ShouldBe("1");
        savedBooking.BoatRequirements.ShouldBe("Old boat note");
        savedBooking.SpecialRequests.ShouldBe("New special note");
        savedBooking.ItineraryStops.Count.ShouldBe(1);
    }

    [Test]
    public async Task UpdateRejectsDuplicateActiveRequestWithExistingBookingCode()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-UP-DUP-FROM", "Bến đi");
        var toStation = WaterbusStation("ST-UP-DUP-TO", "Bến đến");
        var stopStation = WaterbusStation("ST-UP-DUP-STOP", "Bến dừng");
        var existingBooking = DuplicateCharterBooking(
            user,
            fromStation,
            toStation,
            "CB-UP-DUP-001",
            BookingStatus.PendingQuote);
        existingBooking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = existingBooking,
            BookingId = existingBooking.Id,
            Station = stopStation,
            StationId = stopStation.Id,
            StopOrder = 1,
            StayDurationMinutes = 20
        });
        var bookingToUpdate = DuplicateCharterBooking(
            user,
            fromStation,
            toStation,
            "CB-UP-DUP-002",
            BookingStatus.PendingQuote);
        bookingToUpdate.DepartureDate = new DateOnly(2026, 7, 21);
        bookingToUpdate.StartTime = new TimeOnly(10, 0);
        bookingToUpdate.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = bookingToUpdate,
            BookingId = bookingToUpdate.Id,
            Station = stopStation,
            StationId = stopStation.Id,
            StopOrder = 1,
            StayDurationMinutes = 30
        });
        context.AddRange(role, user, fromStation, toStation, stopStation, existingBooking, bookingToUpdate);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingCommand(
                    bookingToUpdate.Id,
                    new DateOnly(2026, 7, 20),
                    BoatRentalUnit.Day,
                    1,
                    AdultCount: 4,
                    ChildCount: 1,
                    StartTime: new TimeOnly(9, 0),
                    FromStationId: fromStation.Id,
                    ToStationId: toStation.Id,
                    ItineraryStops:
                    [
                        new CreateCharterBookingItineraryStopRequest(stopStation.Id, 1, 20, "New note")
                    ],
                    RequestedBoats:
                    [
                        new CreateCharterBookingBoatRequest(1),
                        new CreateCharterBookingBoatRequest(2)
                    ]),
                CancellationToken.None));

        exception.Errors["duplicateBooking"].Single()
            .ShouldContain("CB-UP-DUP-001");
    }

    [Test]
    public async Task UpdateCanRemoveSelectedInsurance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var fromStation = WaterbusStation("ST-INS-REMOVE", "Bến đi");
        var insurancePackage = CharterInsurancePackage(unitPremiumAmount: 10_000m);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-INS-REMOVE",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = user.Email,
            FromStation = fromStation,
            FromStationId = fromStation.Id,
            DepartureDate = new DateOnly(2026, 7, 20),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 10,
            ChildCount = 2,
            PassengerCount = 12,
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid",
            InsuranceSnapshot = InsuranceSnapshot(insurancePackage, quantity: 12)
        };
        context.AddRange(role, user, fromStation, insurancePackage, booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)));

        var detail = await handler.Handle(
            new UpdateCharterBookingCommand(
                booking.Id,
                InsuranceSelected: false),
            CancellationToken.None);

        detail.InsuranceSelected.ShouldBeFalse();
        detail.InsurancePackageId.ShouldBeNull();
        detail.Insurance.ShouldBeNull();
        context.Set<Booking>().Single(x => x.Id == booking.Id).InsuranceSnapshot.ShouldBeNull();
    }

    private sealed class FixedBookingCodeGenerator(string bookingCode) : IBookingCodeGenerator
    {
        public Task<string> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(bookingCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static Station WaterbusStation(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active,
            IsWaterbusStation = true
        };

    private static Booking DuplicateCharterBooking(
        User user,
        Station fromStation,
        Station toStation,
        string bookingCode,
        BookingStatus status) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = bookingCode,
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = user.Email,
            FromStation = fromStation,
            FromStationId = fromStation.Id,
            ToStation = toStation,
            ToStationId = toStation.Id,
            DepartureDate = new DateOnly(2026, 7, 20),
            StartTime = new TimeOnly(9, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 4,
            ChildCount = 1,
            PassengerCount = 5,
            RequestedBoatCount = 2,
            RequestedBoatDecks = "1,2",
            BookingStatus = status,
            PaymentStatus = "Unpaid"
        };

    private static CreateCharterBookingCommand DuplicateCreateCommand(
        Guid fromStationId,
        Guid toStationId,
        Guid stopStationId,
        string? contactPhone = null,
        string? contactEmail = null) =>
        new(
            new DateOnly(2026, 7, 20),
            BoatRentalUnit.Day,
            1,
            AdultCount: 4,
            ChildCount: 1,
            StartTime: new TimeOnly(9, 0),
            FromStationId: fromStationId,
            ToStationId: toStationId,
            ItineraryStops:
            [
                new CreateCharterBookingItineraryStopRequest(stopStationId, 1, 20, "New note")
            ],
            RequestedBoats:
            [
                new CreateCharterBookingBoatRequest(1),
                new CreateCharterBookingBoatRequest(2)
            ],
            ContactPhone: contactPhone,
            ContactEmail: contactEmail);

    private static InsurancePackage CharterInsurancePackage(decimal unitPremiumAmount) =>
        new()
        {
            Code = $"CHARTER_INS_{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            Name = "Bao hiem hanh khach thue tau",
            BookingType = Booking.CharterBookingType,
            IsRequired = false,
            ProviderName = "Bao hiem mac dinh",
            UnitPremiumAmount = unitPremiumAmount,
            CoverageAmount = 50_000_000m,
            Currency = "VND",
            Conditions = ["Chi ap dung cho hanh khach co ten trong danh sach chuyen di."],
            IsActive = true,
            DisplayOrder = 1
        };

    private static BookingInsuranceSnapshot InsuranceSnapshot(
        InsurancePackage package,
        int quantity) =>
        new()
        {
            InsurancePackageId = package.Id,
            Code = package.Code,
            Name = package.Name,
            BookingType = package.BookingType,
            IsRequired = package.IsRequired,
            ProviderName = package.ProviderName,
            ProviderLogoUrl = package.ProviderLogoUrl,
            UnitPremiumAmount = package.UnitPremiumAmount,
            CoverageAmount = package.CoverageAmount,
            Currency = package.Currency,
            Conditions = package.Conditions,
            TermsUrl = package.TermsUrl,
            Quantity = quantity,
            TotalAmount = package.UnitPremiumAmount * quantity,
            QuotedAt = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)
        };
}
