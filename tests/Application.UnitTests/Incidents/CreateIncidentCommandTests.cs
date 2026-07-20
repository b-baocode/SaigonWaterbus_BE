using NUnit.Framework;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Incidents;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Incidents;

public class CreateIncidentCommandTests
{
    [Test]
    public async Task StaffCanReportIncidentForBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var now = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var handler = new CreateIncidentCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new CreateIncidentCommand(
                boat.Id,
                null,
                "MechanicalFailure",
                "Tau bi hong dong co tai ben.",
                "Medium",
                null),
            CancellationToken.None);

        result.BoatId.ShouldBe(boat.Id);
        result.ReportedByUserId.ShouldBe(staffContext.UserId!.Value);
        result.ResolutionStatus.ShouldBe(IncidentSupport.OpenStatus);
        result.OccurredAt.ShouldBe(now);

        context.Boats.Single().Status.ShouldBe(BoatStatus.Incident);
        context.Boats.Single().MaintenanceStartedAt.ShouldBeNull();
        context.Incidents.Single().ResolutionStatus.ShouldBe(IncidentSupport.OpenStatus);
    }

    [Test]
    public async Task HighSeverityIncidentCancelsRelatedTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        var route = Route("R1");
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-20300101-01",
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 8, 30, 0, TimeSpan.FromHours(7)),
            CapacitySnapshot = 20,
            TripStatus = TripStatus.Scheduled
        };
        context.AddRange(boat, route, trip);
        await context.SaveChangesAsync();

        var handler = new CreateIncidentCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new CreateIncidentCommand(
                boat.Id,
                trip.Id,
                "MechanicalFailure",
                "Tau bi hong dong co tren chuyen.",
                "High",
                null),
            CancellationToken.None);

        result.TripId.ShouldBe(trip.Id);

        var savedTrip = context.Trips.Single();
        savedTrip.TripStatus.ShouldBe(TripStatus.Cancelled);
        savedTrip.StatusNote.ShouldBe("Incident MechanicalFailure: Tau bi hong dong co tren chuyen.");
        context.Boats.Single().Status.ShouldBe(BoatStatus.Incident);
        context.Boats.Single().MaintenanceStartedAt.ShouldBeNull();
    }

    [Test]
    public async Task ManagerCanDispatchRescueBoatForIncidentWithoutPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau can cuu ho tren song.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        context.AddRange(incidentBoat, rescueBoat, incident);
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero);
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(assignedAt));

        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: null,
                Note: "Dieu tau den ho tro."),
            CancellationToken.None);

        result.TripId.ShouldBeNull();
        result.RescueBoatId.ShouldBe(rescueBoat.Id);
        result.RescueBoatName.ShouldBe(rescueBoat.Name);
        result.ReplacementBoatId.ShouldBeNull();
        result.ActiveTicketCount.ShouldBe(0);

        var savedIncident = context.Incidents.Single();
        savedIncident.RescueBoatId.ShouldBe(rescueBoat.Id);
        savedIncident.RescueDispatchedAt.ShouldBe(assignedAt);
        savedIncident.RescueDispatchedByUserId.ShouldBe(managerContext.UserId!.Value);
        savedIncident.ReplacementBoatId.ShouldBeNull();
        savedIncident.ReplacementAssignedAt.ShouldBeNull();
        savedIncident.ReplacementAssignedByUserId.ShouldBeNull();
        savedIncident.ReplacementNote.ShouldBe("Dieu tau den ho tro.");
    }

    [Test]
    public async Task AssignReplacementBoatPublishesGpsHookWithBoatCodesAndLocation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB_001");
        var rescueBoat = RescueBoat("RS_001");
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau can cuu ho tren song.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        context.AddRange(
            incidentBoat,
            rescueBoat,
            incident,
            new BoatLatestLocation
            {
                BoatId = incidentBoat.Id,
                GpsDeviceId = Guid.NewGuid(),
                Latitude = 10.7765m,
                Longitude = 106.7065m,
                RecordedAt = new DateTimeOffset(2030, 1, 1, 1, 59, 0, TimeSpan.Zero),
                ReceivedAt = new DateTimeOffset(2030, 1, 1, 1, 59, 1, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2030, 1, 1, 1, 59, 1, TimeSpan.Zero)
            });
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero);
        var gpsHook = new CapturingIncidentGpsHookNotifier();
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(assignedAt),
            gpsHookNotifier: gpsHook);

        await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: null,
                Note: "Dieu tau den ho tro."),
            CancellationToken.None);

        gpsHook.Notifications.Count.ShouldBe(1);
        var notification = gpsHook.Notifications.Single();
        notification.Event.ShouldBe(IncidentSupport.RescueDispatchedEvent);
        notification.IncidentId.ShouldBe(incident.Id);
        notification.BoatCode.ShouldBe("WB_001");
        notification.RescueBoatCode.ShouldBe("RS_001");
        notification.ReplacementBoatCode.ShouldBeNull();
        notification.Lat.ShouldBe(10.7765m);
        notification.Lng.ShouldBe(106.7065m);
    }

    [Test]
    public async Task IncidentWithActiveTicketsRequiresPassengerReplacementBoatAndRescueBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var replacementBoat = Boat("WB-02");
        var route = Route("R1");
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            TripCode = "TR-20300101-01",
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 8, 30, 0, TimeSpan.FromHours(7)),
            CapacitySnapshot = 20,
            TripStatus = TripStatus.Delayed
        };
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            Trip = trip,
            TripId = trip.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau can cuu ho tren song.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-INCIDENT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid"
        };
        var ticket = new Ticket
        {
            Booking = booking,
            TicketCode = "TICKET-1",
            QrToken = "QR-1",
            TicketStatus = TicketStatus.Active,
            IssuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        context.AddRange(incidentBoat, rescueBoat, replacementBoat, route, trip, incident, booking, ticket);
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero);
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(assignedAt));

        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                replacementBoat.Id,
                DelayMinutes: 20,
                Note: "Dieu tau cuu ho va tau thay the."),
            CancellationToken.None);

        result.ActiveTicketCount.ShouldBe(1);
        result.RescueBoatId.ShouldBe(rescueBoat.Id);
        result.ReplacementBoatId.ShouldBe(replacementBoat.Id);

        var savedIncident = context.Incidents.Single();
        savedIncident.RescueBoatId.ShouldBe(rescueBoat.Id);
        savedIncident.ReplacementBoatId.ShouldBe(replacementBoat.Id);

        var savedTrip = context.Trips.Single();
        savedTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedTrip.TripStatus.ShouldBe(TripStatus.Delayed);
    }

    [Test]
    public async Task FuturePassengersRequireReplacementBoatToNextBoardingStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var replacementBoat = Boat("WB-02");
        var route = Route("R1");
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var stationD = Station("D", "Ben D");
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            TripCode = "TR-20300101-01",
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)),
            CapacitySnapshot = 20,
            TripStatus = TripStatus.InProgress
        };
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            Trip = trip,
            TripId = trip.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau hong o doan B-C.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        var booking = new Booking
        {
            BookingCode = "BK-FUTURE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid"
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Nguyen Van A",
            FromStation = stationC,
            FromStationId = stationC.Id,
            ToStation = stationD,
            ToStationId = stationD.Id,
            FromStopOrder = 3,
            ToStopOrder = 4
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = "TICKET-FUTURE",
            QrToken = "QR-FUTURE",
            TicketStatus = TicketStatus.Active,
            IssuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        context.AddRange(
            incidentBoat,
            rescueBoat,
            replacementBoat,
            route,
            stationA,
            stationB,
            stationC,
            stationD,
            RouteStop(route, stationA, 1),
            RouteStop(route, stationB, 2),
            RouteStop(route, stationC, 3),
            RouteStop(route, stationD, 4),
            trip,
            incident,
            booking,
            passenger,
            ticket,
            new BoatLatestLocation
            {
                BoatId = incidentBoat.Id,
                GpsDeviceId = Guid.NewGuid(),
                TripId = trip.Id,
                NextStationId = stationC.Id,
                Latitude = 10.7765m,
                Longitude = 106.7065m,
                RecordedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 0, TimeSpan.FromHours(7)),
                ReceivedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 1, TimeSpan.FromHours(7)),
                UpdatedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 1, TimeSpan.FromHours(7))
            });
        await context.SaveChangesAsync();

        var gpsHook = new CapturingIncidentGpsHookNotifier();
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero)),
            gpsHookNotifier: gpsHook);

        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                replacementBoat.Id,
                DelayMinutes: 15,
                Note: null),
            CancellationToken.None);

        result.ActiveTicketCount.ShouldBe(1);
        result.OnboardPassengerCount.ShouldBe(0);
        result.FuturePassengerCount.ShouldBe(1);
        result.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.ContinueFromStation);
        result.ReplacementTargetStationId.ShouldBe(stationC.Id);
        result.ReplacementTargetStationName.ShouldBe("Ben C");
        result.ReplacementTargetStopOrder.ShouldBe(3);

        var savedIncident = context.Incidents.Single();
        savedIncident.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.ContinueFromStation);
        savedIncident.ReplacementTargetStationId.ShouldBe(stationC.Id);
        savedIncident.OnboardPassengerCountSnapshot.ShouldBe(0);
        savedIncident.FuturePassengerCountSnapshot.ShouldBe(1);
        savedIncident.ReplacementNote.ShouldNotBeNull();
        savedIncident.ReplacementNote.ShouldContain("Ben C");

        var notification = gpsHook.Notifications.Single();
        notification.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.ContinueFromStation);
        notification.ReplacementTargetStationId.ShouldBe(stationC.Id);
        notification.ReplacementTargetStationName.ShouldBe("Ben C");
        notification.FuturePassengerCount.ShouldBe(1);
    }

    [Test]
    public async Task CompletedPastPassengersDoNotRequireReplacementBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var route = Route("R1");
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            TripCode = "TR-20300101-01",
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 8, 45, 0, TimeSpan.FromHours(7)),
            CapacitySnapshot = 20,
            TripStatus = TripStatus.InProgress
        };
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            Trip = trip,
            TripId = trip.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau hong o doan B-C.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        var booking = new Booking
        {
            BookingCode = "BK-PAST",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid"
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            Trip = trip,
            TripId = trip.Id,
            FullName = "Nguyen Van A",
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id,
            FromStopOrder = 1,
            ToStopOrder = 2
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = "TICKET-PAST",
            QrToken = "QR-PAST",
            TicketStatus = TicketStatus.Active,
            IssuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        context.AddRange(
            incidentBoat,
            rescueBoat,
            route,
            stationA,
            stationB,
            stationC,
            RouteStop(route, stationA, 1),
            RouteStop(route, stationB, 2),
            RouteStop(route, stationC, 3),
            trip,
            incident,
            booking,
            passenger,
            ticket,
            new BoatLatestLocation
            {
                BoatId = incidentBoat.Id,
                GpsDeviceId = Guid.NewGuid(),
                TripId = trip.Id,
                NextStationId = stationC.Id,
                Latitude = 10.7765m,
                Longitude = 106.7065m,
                RecordedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 0, TimeSpan.FromHours(7)),
                ReceivedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 1, TimeSpan.FromHours(7)),
                UpdatedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 1, TimeSpan.FromHours(7))
            });
        await context.SaveChangesAsync();

        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: null,
                Note: "Chi dieu tau cuu ho."),
            CancellationToken.None);

        result.ActiveTicketCount.ShouldBe(1);
        result.OnboardPassengerCount.ShouldBe(0);
        result.FuturePassengerCount.ShouldBe(0);
        result.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.None);
        result.ReplacementBoatId.ShouldBeNull();

        var savedIncident = context.Incidents.Single();
        savedIncident.ReplacementBoatId.ShouldBeNull();
        savedIncident.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.None);
    }

    [Test]
    public async Task GpsCallbackCompletesRescueMissionAndMovesIncidentBoatToMaintenance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var incidentBoat = Boat("WB_005");
        incidentBoat.Status = BoatStatus.Incident;
        var rescueBoat = RescueBoat("SOS_001");
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            RescueBoat = rescueBoat,
            RescueBoatId = rescueBoat.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau can keo ve ben.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        context.AddRange(incidentBoat, rescueBoat, incident);
        await context.SaveChangesAsync();

        var completedAt = new DateTimeOffset(2030, 1, 1, 3, 0, 0, TimeSpan.Zero);
        var gpsHook = new CapturingIncidentGpsHookNotifier();
        var handler = new CompleteRescueMissionCommandHandler(
            context,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 4, 0, 0, TimeSpan.Zero)),
            gpsHookNotifier: gpsHook);

        var result = await handler.Handle(
            new CompleteRescueMissionCommand(
                incident.Id,
                "WB_005",
                "SOS_001",
                completedAt,
                "Tau da duoc keo ve ben."),
            CancellationToken.None);

        result.ResolutionStatus.ShouldBe(IncidentSupport.ResolvedStatus);
        result.ResolvedAt.ShouldBe(completedAt);
        result.ResolutionNote.ShouldBe("Tau da duoc keo ve ben.");

        var savedIncidentBoat = context.Boats.Single(x => x.Id == incidentBoat.Id);
        savedIncidentBoat.Status.ShouldBe(BoatStatus.UnderMaintenance);
        savedIncidentBoat.MaintenanceStartedAt.ShouldBe(completedAt);

        var savedRescueBoat = context.Boats.Single(x => x.Id == rescueBoat.Id);
        savedRescueBoat.Status.ShouldBe(BoatStatus.Active);

        gpsHook.Notifications.Count.ShouldBe(1);
        gpsHook.Notifications.Single().Event.ShouldBe(IncidentSupport.IncidentResolvedEvent);
    }

    private static Boat Boat(string code) =>
        new()
        {
            Code = code,
            Name = code,
            Status = BoatStatus.Active,
            SeatCount = 20,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            SeatsConfigured = true
        };

    private static Boat RescueBoat(string code) =>
        new()
        {
            Code = code,
            Name = code,
            ServiceType = BoatServiceType.Rescue,
            Status = BoatStatus.Active,
            SeatCount = 0,
            NumberOfDecks = 1,
            SeatSetupType = SeatSetupType.FullStandard,
            SeatsConfigured = false
        };

    private static Route Route(string code) =>
        new()
        {
            RouteCode = code,
            RouteName = code,
            Status = "Active"
        };

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Latitude = 10.7000000m,
            Longitude = 106.7000000m
        };

    private static RouteStop RouteStop(Route route, Station station, int stopOrder) =>
        new()
        {
            Route = route,
            RouteId = route.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CapturingIncidentGpsHookNotifier : IIncidentGpsHookNotifier
    {
        public List<IncidentGpsHookNotification> Notifications { get; } = [];

        public Task NotifyAsync(
            IncidentGpsHookNotification notification,
            CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
