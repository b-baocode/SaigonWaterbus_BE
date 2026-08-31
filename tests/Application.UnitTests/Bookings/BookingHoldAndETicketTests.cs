using System.Globalization;
using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

public class BookingHoldAndETicketTests
{
    [Test]
    public async Task WebhookPaidRegularBookingSendsETicketEmailsWithBookingQr()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            BookingCode = "BK-ETICKET",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 20000,
            TotalAmount = 20000,
            RemainingAmount = 20000
        };
        var passengerWithEmail = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van B",
            PassengerType = "ADULT",
            Email = "passenger-b@gmail.com",
            UnitPrice = 10000
        };
        var passengerWithoutEmail = new BookingPassenger
        {
            Booking = booking,
            FullName = "Nguyen Van C",
            PassengerType = "CHILD",
            UnitPrice = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000001",
            Provider = "PayOS",
            Amount = 20000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(booking, passengerWithEmail, passengerWithoutEmail, payment);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new PaidPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(
            new HandlePaymentWebhookCommand(CreatePaidWebhook(2000001, 20000)),
            CancellationToken.None);

        // QR chung được sinh với prefix BK.
        booking.CharterBookingQrToken.ShouldNotBeNullOrWhiteSpace();
        booking.CharterBookingQrToken.ShouldStartWith("BK");

        sender.ETickets.Count.ShouldBe(2);

        // Người đặt nhận 1 email vé tổng chứa cả 2 vé + QR chung.
        var eTicket = sender.ETickets.Single(x => x.Booking.Email == "booker@gmail.com");
        eTicket.Booking.Email.ShouldBe("booker@gmail.com");
        eTicket.BookingQrToken.ShouldBe(booking.CharterBookingQrToken);
        eTicket.Tickets.Count.ShouldBe(2);
        eTicket.Tickets.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.QrToken));

        // Hành khách có email nhận thêm e-ticket riêng; hành khách không email thì không.
        var passengerETicket = sender.ETickets.Single(x => x.Booking.Email == "passenger-b@gmail.com");
        passengerETicket.BookingQrToken.ShouldBeNull();
        passengerETicket.Tickets.ShouldHaveSingleItem();
        var passengerTicket = context.Tickets.Single(x => x.BookingPassengerId == passengerWithEmail.Id);
        passengerETicket.Tickets.Single().QrToken.ShouldBe(passengerTicket.QrToken);
        sender.BoardingPasses.ShouldBeEmpty();

        // Không gửi email xác nhận thanh toán kiểu cũ cho booking thường.
        sender.Notifications.ShouldBeEmpty();

        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var manifestByBookingCode = await new GetBookingManifestQueryHandler(context, adminContext)
            .Handle(new GetBookingManifestByCodeOrQrTokenQuery("BK-ETICKET"), CancellationToken.None);
        manifestByBookingCode.BookingId.ShouldBe(booking.Id);
        manifestByBookingCode.BookingQrToken.ShouldBe(booking.CharterBookingQrToken);
    }

    [Test]
    public async Task WebhookPaidRoundTripBookingSendsSeparateBookerETicketsPerLeg()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var outboundTrip = CreateTrip("TR-OUT");
        var returnTrip = CreateTrip("TR-RET");
        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            Trip = outboundTrip,
            ReturnTrip = returnTrip,
            BookingCode = "BK-ROUNDTRIP-ET",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 20000,
            TotalAmount = 20000,
            RemainingAmount = 20000
        };
        var outboundPassenger = new BookingPassenger
        {
            Booking = booking,
            Trip = outboundTrip,
            FullName = "Khach Chieu Di",
            PassengerType = "ADULT",
            UnitPrice = 10000
        };
        var returnPassenger = new BookingPassenger
        {
            Booking = booking,
            Trip = returnTrip,
            FullName = "Khach Chieu Ve",
            PassengerType = "ADULT",
            Email = "passenger-return@gmail.com",
            UnitPrice = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000002",
            Provider = "PayOS",
            Amount = 20000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(outboundTrip, returnTrip, booking, outboundPassenger, returnPassenger, payment);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new PaidPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(
            new HandlePaymentWebhookCommand(CreatePaidWebhook(2000002, 20000)),
            CancellationToken.None);

        // Email tổng cho người đặt được tách theo chiều, không gộp cả 2 chuyến vào một email/PDF.
        var bookerETickets = sender.ETickets
            .Where(x => x.Booking.Email == "booker@gmail.com")
            .OrderBy(x => x.TripCode)
            .ToList();
        bookerETickets.Count.ShouldBe(2);

        var outboundBookerETicket = bookerETickets.Single(x => x.TripCode == "TR-OUT");
        outboundBookerETicket.BookingQrToken.ShouldBe(booking.CharterBookingQrToken);
        outboundBookerETicket.Legs.ShouldBeNull();
        outboundBookerETicket.Tickets.ShouldHaveSingleItem().PassengerName.ShouldBe("Khach Chieu Di");

        var returnBookerETicket = bookerETickets.Single(x => x.TripCode == "TR-RET");
        returnBookerETicket.BookingQrToken.ShouldBe(booking.CharterBookingQrToken);
        returnBookerETicket.Legs.ShouldBeNull();
        returnBookerETicket.Tickets.ShouldHaveSingleItem().PassengerName.ShouldBe("Khach Chieu Ve");

        // Email riêng của hành khách chiều về hiển thị trip chiều về.
        var passengerETicket = sender.ETickets.Single(x => x.Booking.Email == "passenger-return@gmail.com");
        passengerETicket.TripCode.ShouldBe("TR-RET");
        passengerETicket.Legs.ShouldBeNull();
        passengerETicket.Tickets.ShouldHaveSingleItem().PassengerName.ShouldBe("Khach Chieu Ve");
    }

    [Test]
    public async Task LapInfantUsesCompanionTicketAndScanShowsBothPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var passengerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var stationA = new Station { StationCode = "BD", StationName = "Bến Bạch Đằng" };
        var stationB = new Station { StationCode = "BA", StationName = "Bến Bình An" };
        var route = new Route
        {
            RouteCode = "R-BD-BA",
            RouteName = "Bạch Đằng - Bình An",
            RouteType = RouteTypes.Regular
        };
        route.RouteStops.Add(new RouteStop { Route = route, Station = stationA, StationId = stationA.Id, StopOrder = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = stationB, StationId = stationB.Id, StopOrder = 2, DistanceFromPreviousKm = 3m });
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        var seat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-INFANT-PAIR",
            TripType = TripTypes.Regular,
            OperatingDate = new DateOnly(2026, 7, 20),
            DepartureTime = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 1
        };
        var tripSeat = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seat, SeatId = seat.Id, Status = TripSeat.StatusBooked };
        var booking = new Booking
        {
            UserId = passengerContext.UserId,
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-INFANT-PAIR",
            ContactName = "Nguyen Huu Hoang",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 9500,
            TotalAmount = 9500,
            RemainingAmount = 9500
        };
        var adult = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            TripSeat = tripSeat,
            TripSeatId = tripSeat.Id,
            FullName = "Nguoi Lon Di Kem",
            PassengerType = "ADULT",
            UnitPrice = 9500,
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id,
            FromStopOrder = 1,
            ToStopOrder = 2
        };
        var infant = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Em bé",
            PassengerType = "INFANT",
            BirthYear = 2026,
            UnitPrice = 0,
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id,
            FromStopOrder = 1,
            ToStopOrder = 2
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000003",
            Provider = "PayOS",
            Amount = 9500,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(route, boat, seat, trip, tripSeat, booking, adult, infant, payment);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new PaidPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(
            new HandlePaymentWebhookCommand(CreatePaidWebhook(2000003, 9500)),
            CancellationToken.None);

        var ticket = context.Tickets.ShouldHaveSingleItem();
        ticket.BookingPassengerId.ShouldBe(adult.Id);
        var bookerTickets = sender.ETickets.Single(x => x.Booking.Email == "booker@gmail.com")
            .Tickets;
        bookerTickets.Count.ShouldBe(2);
        bookerTickets.ShouldAllBe(x => x.TicketCode == ticket.TicketCode);
        bookerTickets.ShouldContain(x => x.PassengerName == "Nguoi Lon Di Kem");
        bookerTickets.ShouldContain(x => x.PassengerName == "Em bé");
        bookerTickets.Single(x => x.PassengerName == "Em bé").CompanionPassengerName
            .ShouldBe("Nguoi Lon Di Kem");

        var manifest = await new GetBookingManifestQueryHandler(context, passengerContext)
            .Handle(new GetBookingManifestByQrTokenQuery(booking.CharterBookingQrToken!), CancellationToken.None);
        manifest.PassengerCount.ShouldBe(2);
        manifest.ActiveTicketCount.ShouldBe(1);
        var adultManifest = manifest.Passengers.Single(x => x.PassengerId == adult.Id);
        var infantManifest = manifest.Passengers.Single(x => x.PassengerId == infant.Id);
        adultManifest.TicketCode.ShouldBe(ticket.TicketCode);
        infantManifest.TicketCode.ShouldBe(ticket.TicketCode);
        infantManifest.IsLapInfant.ShouldBeTrue();
        infantManifest.UsesCompanionTicket.ShouldBeTrue();
        infantManifest.CompanionPassengerId.ShouldBe(adult.Id);
        infantManifest.CompanionPassengerName.ShouldBe("Nguoi Lon Di Kem");

        var detail = await new GetBookingDetailQueryHandler(context, passengerContext)
            .Handle(new GetBookingDetailQuery(booking.Id), CancellationToken.None);
        var infantDetail = detail.Items.Single(x => x.BookingItemId == infant.Id);
        infantDetail.TicketCode.ShouldBe(ticket.TicketCode);
        infantDetail.TicketQrToken.ShouldBe(ticket.QrToken);
        infantDetail.IsLapInfant.ShouldBeTrue();
        infantDetail.UsesCompanionTicket.ShouldBeTrue();
        infantDetail.CompanionPassengerId.ShouldBe(adult.Id);
        infantDetail.CompanionPassengerName.ShouldBe("Nguoi Lon Di Kem");

        var endedDetail = await new GetBookingDetailQueryHandler(
                context,
                passengerContext,
                new FixedTimeProvider(trip.ArrivalTime.AddSeconds(1)))
            .Handle(new GetBookingDetailQuery(booking.Id), CancellationToken.None);
        endedDetail.Items.Single(x => x.BookingItemId == adult.Id).TicketStatus.ShouldBe("Used");
        endedDetail.Items.Single(x => x.BookingItemId == infant.Id).TicketStatus.ShouldBe("Used");
        ticket.TicketStatus.ShouldBe(TicketStatus.Active);

        var scan = await new ScanTicketQueryHandler(context, adminContext, TimeProvider.System)
            .Handle(new ScanTicketQuery(ticket.QrToken), CancellationToken.None);
        scan.PassengerCount.ShouldBe(2);
        scan.Passengers.Select(x => x.FullName).ShouldBe(["Nguoi Lon Di Kem", "Em bé"]);
        scan.Passengers.Single(x => x.PassengerId == infant.Id).IsLapInfant.ShouldBeTrue();
        scan.Passengers.Single(x => x.PassengerId == infant.Id).UsesCompanionTicket.ShouldBeTrue();
        scan.Passengers.Single(x => x.PassengerId == infant.Id).CompanionPassengerId.ShouldBe(adult.Id);
        scan.Passengers.Single(x => x.PassengerId == infant.Id).CompanionPassengerName
            .ShouldBe("Nguoi Lon Di Kem");

        var onboardBeforeCheckIn = await TripPassengerCountSupport.LoadOnboardPassengerCountAsync(
            context,
            trip.Id,
            CancellationToken.None);
        onboardBeforeCheckIn.ShouldBe(0);

        ticket.TicketStatus = TicketStatus.CheckedIn;
        ticket.CheckedInAt = trip.DepartureTime;
        await context.SaveChangesAsync();

        var onboardAfterCheckIn = await TripPassengerCountSupport.LoadOnboardPassengerCountAsync(
            context,
            trip.Id,
            CancellationToken.None);
        onboardAfterCheckIn.ShouldBe(2);

        ticket.TicketStatus = TicketStatus.CheckedOut;
        ticket.CheckedOutAt = trip.ArrivalTime;
        await context.SaveChangesAsync();

        var onboardAfterCheckOut = await TripPassengerCountSupport.LoadOnboardPassengerCountAsync(
            context,
            trip.Id,
            CancellationToken.None);
        onboardAfterCheckOut.ShouldBe(0);
    }

    [Test]
    public async Task ChildHasOwnTicketAndQrButRequiresAdultInBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var passengerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var stationA = new Station { StationCode = "BD", StationName = "Bến Bạch Đằng" };
        var stationB = new Station { StationCode = "BA", StationName = "Bến Bình An" };
        var route = new Route
        {
            RouteCode = "R-BD-BA-CHILD",
            RouteName = "Bạch Đằng - Bình An",
            RouteType = RouteTypes.Regular
        };
        route.RouteStops.Add(new RouteStop { Route = route, Station = stationA, StationId = stationA.Id, StopOrder = 1 });
        route.RouteStops.Add(new RouteStop { Route = route, Station = stationB, StationId = stationB.Id, StopOrder = 2, DistanceFromPreviousKm = 3m });
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        boat.SeatCount = 2;
        var adultSeat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };
        var childSeat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A2", Deck = 1, Row = "A", Column = 2 };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-CHILD-PAIR",
            TripType = TripTypes.Regular,
            OperatingDate = new DateOnly(2026, 7, 20),
            DepartureTime = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 2
        };
        var adultTripSeat = new TripSeat { Trip = trip, TripId = trip.Id, Seat = adultSeat, SeatId = adultSeat.Id, Status = TripSeat.StatusBooked };
        var childTripSeat = new TripSeat { Trip = trip, TripId = trip.Id, Seat = childSeat, SeatId = childSeat.Id, Status = TripSeat.StatusBooked };
        var booking = new Booking
        {
            UserId = passengerContext.UserId,
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-CHILD-PAIR",
            ContactName = "Nguyen Huu Hoang",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 9500,
            TotalAmount = 9500,
            RemainingAmount = 9500
        };
        var adult = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            TripSeat = adultTripSeat,
            TripSeatId = adultTripSeat.Id,
            FullName = "Nguyen Huu Hoang",
            PassengerType = "ADULT",
            UnitPrice = 9500,
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id,
            FromStopOrder = 1,
            ToStopOrder = 2
        };
        var child = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            TripSeat = childTripSeat,
            TripSeatId = childTripSeat.Id,
            FullName = "Bé lớn",
            PassengerType = "CHILD",
            BirthYear = 2020,
            UnitPrice = 0,
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id,
            FromStopOrder = 1,
            ToStopOrder = 2
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = "2000004",
            Provider = "PayOS",
            Amount = 9500,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };
        context.AddRange(route, boat, adultSeat, childSeat, trip, adultTripSeat, childTripSeat, booking, adult, child, payment);
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        var handler = new HandlePaymentWebhookCommandHandler(
            context,
            new PaidPaymentGateway(),
            sender,
            TimeProvider.System);

        await handler.Handle(
            new HandlePaymentWebhookCommand(CreatePaidWebhook(2000004, 9500)),
            CancellationToken.None);

        var tickets = context.Tickets.OrderBy(x => x.BookingPassengerId).ToList();
        tickets.Count.ShouldBe(2);
        var adultTicket = tickets.Single(x => x.BookingPassengerId == adult.Id);
        var childTicket = tickets.Single(x => x.BookingPassengerId == child.Id);
        childTicket.TicketCode.ShouldNotBe(adultTicket.TicketCode);
        var bookerTickets = sender.ETickets.Single(x => x.Booking.Email == "booker@gmail.com").Tickets;
        bookerTickets.Count.ShouldBe(2);
        var expectedTicketCodes = new[] { adultTicket.TicketCode, childTicket.TicketCode }
            .OrderBy(x => x)
            .ToArray();
        bookerTickets.Select(x => x.TicketCode).OrderBy(x => x).ShouldBe(
            expectedTicketCodes);
        bookerTickets.Single(x => x.PassengerName == "Bé lớn").SeatCode.ShouldBe("A2");

        var manifest = await new GetBookingManifestQueryHandler(context, passengerContext)
            .Handle(new GetBookingManifestByQrTokenQuery(booking.CharterBookingQrToken!), CancellationToken.None);
        manifest.PassengerCount.ShouldBe(2);
        manifest.ActiveTicketCount.ShouldBe(2);
        var childManifest = manifest.Passengers.Single(x => x.PassengerId == child.Id);
        childManifest.TicketCode.ShouldBe(childTicket.TicketCode);
        childManifest.IsLapInfant.ShouldBeFalse();
        childManifest.UsesCompanionTicket.ShouldBeFalse();
        childManifest.CompanionPassengerId.ShouldBeNull();

        var adultScan = await new ScanTicketQueryHandler(context, adminContext, TimeProvider.System)
            .Handle(new ScanTicketQuery(adultTicket.QrToken), CancellationToken.None);
        adultScan.PassengerCount.ShouldBe(1);
        adultScan.Passengers.ShouldHaveSingleItem().PassengerId.ShouldBe(adult.Id);

        var childScan = await new ScanTicketQueryHandler(context, adminContext, TimeProvider.System)
            .Handle(new ScanTicketQuery(childTicket.QrToken), CancellationToken.None);
        childScan.PassengerCount.ShouldBe(1);
        var scannedChild = childScan.Passengers.ShouldHaveSingleItem();
        scannedChild.PassengerId.ShouldBe(child.Id);
        scannedChild.IsLapInfant.ShouldBeFalse();
        scannedChild.UsesCompanionTicket.ShouldBeFalse();
        scannedChild.CompanionPassengerId.ShouldBeNull();
        scannedChild.SeatCode.ShouldBe("A2");
    }

    private static Trip CreateTrip(string tripCode) =>
        new()
        {
            Route = new Route
            {
                RouteCode = $"R-{tripCode}",
                RouteName = $"Route {tripCode}"
            },
            TripCode = tripCode,
            OperatingDate = new DateOnly(2026, 7, 20),
            DepartureTime = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 2
        };

    [Test]
    public async Task CreatePaymentExpiresRegularBookingWhenHoldExpired()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var booking = new Booking
        {
            UserId = userId,
            BookingCode = "BK-EXPIRED-HOLD",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000,
            HoldExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(
            context,
            new TestUserContext(userId),
            new PaidPaymentGateway(),
            new RecordingPaymentNotificationSender(),
            TimeProvider.System);

        await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreatePaymentCommand(booking.Id), CancellationToken.None));

        booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        context.Set<Payment>().Count().ShouldBe(0);
    }

    /// <summary>
    /// Tiền về sau khi booking đã Expired nhưng ghế vẫn trống và tàu chưa chạy: hồi sinh bình thường.
    /// </summary>
    [Test]
    public async Task WebhookRevivesExpiredBookingWhenSeatStillFree()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seeded = await SeedLatePaidScenarioAsync(context, "TR-LATE-FREE", "BK-LATE-FREE", 2100001);
        var sender = new RecordingPaymentNotificationSender();

        await new HandlePaymentWebhookCommandHandler(
                context,
                new PaidPaymentGateway(),
                sender,
                new FixedTimeProvider(BeforeDeparture))
            .Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(2100001, 10000)), CancellationToken.None);

        seeded.Booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
        seeded.Payment.PaymentStatus.ShouldBe("Paid");
        seeded.Payment.RefundStatus.ShouldBeNull();
        sender.ETickets.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Ghế đã bị bán cho khách khác trong lúc tiền đang về: KHÔNG hồi sinh, không phát vé,
    /// ghi nhận tiền và mở yêu cầu hoàn.
    /// </summary>
    [Test]
    public async Task WebhookDoesNotReviveExpiredBookingWhenSeatTakenByAnother()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seeded = await SeedLatePaidScenarioAsync(context, "TR-LATE-TAKEN", "BK-LATE-TAKEN", 2100002);

        var otherBooking = new Booking
        {
            UserId = Guid.NewGuid(),
            TripId = seeded.Trip.Id,
            BookingCode = "BK-LATE-WINNER",
            ContactName = "Khach Mua Sau",
            ContactPhone = "0900000002",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 0
        };
        context.AddRange(otherBooking, new BookingPassenger
        {
            Booking = otherBooking,
            TripId = seeded.Trip.Id,
            TripSeatId = seeded.TripSeat.Id,
            FullName = "Khach Mua Sau",
            PassengerType = "ADULT",
            FromStopOrder = 1,
            ToStopOrder = 2,
            UnitPrice = 10000
        });
        await context.SaveChangesAsync();

        var sender = new RecordingPaymentNotificationSender();
        await new HandlePaymentWebhookCommandHandler(
                context,
                new PaidPaymentGateway(),
                sender,
                new FixedTimeProvider(BeforeDeparture))
            .Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(2100002, 10000)), CancellationToken.None);

        seeded.Booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        seeded.Payment.PaymentStatus.ShouldBe("Paid");
        seeded.Payment.RefundStatus.ShouldBe(PaymentSupport.RefundPendingStatus);
        seeded.Payment.RefundRequestedAmount.ShouldBe(seeded.Payment.Amount);
        sender.ETickets.ShouldBeEmpty();
        context.Set<Notification>()
            .Count(x => x.Type == NotificationTypes.BookingPaymentRefundPending)
            .ShouldBe(1);
    }

    /// <summary>Tàu đã rời bến khách lên khi tiền về: vé vô nghĩa nên cũng chuyển sang hoàn tiền.</summary>
    [Test]
    public async Task WebhookDoesNotReviveExpiredBookingAfterBoardingDeparted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var seeded = await SeedLatePaidScenarioAsync(context, "TR-LATE-GONE", "BK-LATE-GONE", 2100003);
        var sender = new RecordingPaymentNotificationSender();

        await new HandlePaymentWebhookCommandHandler(
                context,
                new PaidPaymentGateway(),
                sender,
                new FixedTimeProvider(AfterDeparture))
            .Handle(new HandlePaymentWebhookCommand(CreatePaidWebhook(2100003, 10000)), CancellationToken.None);

        seeded.Booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        seeded.Payment.PaymentStatus.ShouldBe("Paid");
        seeded.Payment.RefundStatus.ShouldBe(PaymentSupport.RefundPendingStatus);
        sender.ETickets.ShouldBeEmpty();
    }

    private static readonly DateTimeOffset BeforeDeparture =
        new(2026, 7, 20, 7, 50, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AfterDeparture =
        new(2026, 7, 20, 8, 10, 0, TimeSpan.Zero);

    private sealed record LatePaidScenario(Trip Trip, TripSeat TripSeat, Booking Booking, Payment Payment);

    /// <summary>
    /// Booking vé thường đã bị đánh Expired vì hết hạn giữ chỗ, payment PayOS còn Pending — đúng
    /// trạng thái ngay trước lúc webhook báo tiền về trễ.
    /// </summary>
    private static async Task<LatePaidScenario> SeedLatePaidScenarioAsync(
        ApplicationDbContext context,
        string tripCode,
        string bookingCode,
        long orderCode)
    {
        var trip = CreateTrip(tripCode);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, BoatStatus.Active);
        var seat = new Seat { Boat = boat, BoatId = boat.Id, Code = "A1", Deck = 1, Row = "A", Column = 1 };
        var tripSeat = new TripSeat { Trip = trip, TripId = trip.Id, Seat = seat, SeatId = seat.Id, Price = 10000m };

        var booking = new Booking
        {
            UserId = Guid.NewGuid(),
            Trip = trip,
            BookingCode = bookingCode,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            ContactEmail = "booker@gmail.com",
            BookingStatus = BookingStatus.Expired,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000,
            HoldExpiresAt = BeforeDeparture.AddMinutes(-1)
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripSeat = tripSeat,
            FullName = "Nguyen Van A",
            PassengerType = "ADULT",
            FromStopOrder = 1,
            ToStopOrder = 2,
            UnitPrice = 10000
        };
        var payment = new Payment
        {
            Booking = booking,
            PaymentCode = orderCode.ToString(CultureInfo.InvariantCulture),
            Provider = "PayOS",
            Amount = 10000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Full",
            PaymentStatus = "Pending"
        };

        context.AddRange(boat, seat, trip, tripSeat, booking, passenger, payment);
        await context.SaveChangesAsync();
        return new LatePaidScenario(trip, tripSeat, booking, payment);
    }

    private static CharterBookingDepositPaymentWebhook CreatePaidWebhook(long orderCode, long amount) =>
        new(
            "00",
            "success",
            true,
            new CharterBookingDepositPaymentWebhookData(
                orderCode,
                amount,
                null,
                null,
                null,
                null,
                "VND",
                "payment-link-id",
                "00",
                "success",
                null,
                null,
                null,
                null,
                null,
                null),
            "signature");

    private sealed class RecordingPaymentNotificationSender : IPaymentNotificationSender
    {
        public List<PaymentSucceededNotification> Notifications { get; } = [];
        public List<BoardingPassNotification> BoardingPasses { get; } = [];
        public List<ETicketNotification> ETickets { get; } = [];

        public Task SendPaymentSucceededAsync(
            PaymentSucceededNotification notification,
            CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendBoardingPassAsync(
            BoardingPassNotification notification,
            CancellationToken cancellationToken)
        {
            BoardingPasses.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken)
        {
            ETickets.Add(notification);
            return Task.CompletedTask;
        }

        public Task SendRefundReleasedAsync(
            RefundReleasedNotification notification,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SendCharterETicketsAsync(
            ETicketNotification notification,
            CancellationToken cancellationToken)
        {
            ETickets.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class PaidPaymentGateway : ICharterBookingPaymentGateway
    {
        public Task<CharterBookingDepositPaymentResult> CreateDepositPaymentAsync(
            CharterBookingDepositPaymentRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingDepositPaymentResult(
                "link-id",
                "https://example.test/checkout",
                "qr",
                "Pending"));

        public Task<CharterBookingPaymentStatusResult> GetPaymentAsync(
            long orderCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingPaymentStatusResult(
                orderCode,
                null,
                "Pending",
                "link-id",
                "https://example.test/checkout"));

        public Task<CharterBookingPaymentCancellationResult> CancelPaymentAsync(
            long orderCode,
            string reason,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingPaymentCancellationResult(
                "payment-link-id",
                "CANCELLED",
                reason));

        public Task<CharterBookingRefundPayoutResult> CreateRefundPayoutAsync(
            CharterBookingRefundPayoutRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CharterBookingRefundPayoutResult("payout-id", "ref", "Pending", null));

        public Task<CharterBookingRefundPayoutResult?> GetRefundPayoutByReferenceIdAsync(
            string referenceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CharterBookingRefundPayoutResult?>(null);

        public bool IsValidWebhook(CharterBookingDepositPaymentWebhook webhook) => true;
    }
}
