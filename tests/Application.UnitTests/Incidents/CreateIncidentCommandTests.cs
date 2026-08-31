using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Incidents;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

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
        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var handler = new CreateIncidentCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now),
            notificationRealtimeNotifier: realtimeNotifier);

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

        var notification = context.Set<Notification>().Single();
        notification.UserId.ShouldBe(staffContext.UserId!.Value);
        notification.Type.ShouldBe(NotificationTypes.IncidentReported);
        notification.RelatedEntityType.ShouldBe(NotificationRelatedEntityTypes.Incident);
        notification.RelatedEntityId.ShouldBe(result.IncidentId);
        notification.Body.ShouldNotBeNull();
        notification.Body.ShouldContain("MechanicalFailure");
        realtimeNotifier.Published.Count.ShouldBe(1);
        realtimeNotifier.Published.Single().NotificationId.ShouldBe(notification.Id);
    }

    [Test]
    public async Task ManagerCannotReportIncident()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var boat = Boat("WB-01");
        context.Boats.Add(boat);
        await context.SaveChangesAsync();

        var handler = new CreateIncidentCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new CreateIncidentCommand(
                    boat.Id,
                    null,
                    "MechanicalFailure",
                    "Manager khong con xu ly su co.",
                    "Medium",
                    null),
                CancellationToken.None));
    }

    [Test]
    public async Task HighSeverityIncidentDelaysRelatedTrip()
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
        savedTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        savedTrip.StatusNote.ShouldBe("Incident MechanicalFailure: Tau bi hong dong co tren chuyen.");
        context.Boats.Single().Status.ShouldBe(BoatStatus.Incident);
        context.Boats.Single().MaintenanceStartedAt.ShouldBeNull();
    }

    [Test]
    public async Task IncidentWithoutTripIdUsesLatestGpsTripForBoat()
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
            TripStatus = TripStatus.InProgress
        };
        var now = new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7));
        context.AddRange(
            boat,
            route,
            trip,
            new BoatLatestLocation
            {
                BoatId = boat.Id,
                GpsDeviceId = Guid.NewGuid(),
                TripId = trip.Id,
                Latitude = 10.7765m,
                Longitude = 106.7065m,
                RecordedAt = now.AddSeconds(-5),
                ReceivedAt = now.AddSeconds(-3),
                UpdatedAt = now.AddSeconds(-3)
            });
        await context.SaveChangesAsync();

        var handler = new CreateIncidentCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        var result = await handler.Handle(
            new CreateIncidentCommand(
                boat.Id,
                TripId: null,
                "MechanicalFailure",
                "Tau bi hong khi dang chay trip.",
                "Medium",
                null),
            CancellationToken.None);

        result.TripId.ShouldBe(trip.Id);
        result.TripCode.ShouldBe(trip.TripCode);

        var savedIncident = context.Incidents.Single();
        savedIncident.TripId.ShouldBe(trip.Id);

        var savedTrip = context.Trips.Single();
        savedTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        savedTrip.StatusNote.ShouldBe("Incident MechanicalFailure: Tau bi hong khi dang chay trip.");
    }

    [Test]
    public async Task IncidentClearsFailedBoatLiveLocationFromTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var boat = Boat("WB-01");
        var route = Route("R1");
        var nextStation = Station("S2", "Station 2");
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
            TripStatus = TripStatus.InProgress
        };
        var now = new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7));
        context.AddRange(
            boat,
            route,
            nextStation,
            trip,
            new BoatLatestLocation
            {
                BoatId = boat.Id,
                GpsDeviceId = Guid.NewGuid(),
                RouteId = route.Id,
                TripId = trip.Id,
                NextStationId = nextStation.Id,
                Latitude = 10.7765m,
                Longitude = 106.7065m,
                RemainingDistanceKmToNextStation = 1.2m,
                RemainingMinutesToNextStation = 4,
                SpeedKmh = 12,
                RecordedAt = now.AddSeconds(-5),
                ReceivedAt = now.AddSeconds(-3),
                UpdatedAt = now.AddSeconds(-3),
                Status = "moving"
            });
        await context.SaveChangesAsync();

        var handler = new CreateIncidentCommandHandler(
            context,
            staffContext,
            new FixedTimeProvider(now));

        await handler.Handle(
            new CreateIncidentCommand(
                boat.Id,
                trip.Id,
                "MechanicalFailure",
                "Tau bi hong khi dang chay trip.",
                "High",
                null),
            CancellationToken.None);

        var latestLocation = context.BoatLatestLocations.Single();
        latestLocation.RouteId.ShouldBeNull();
        latestLocation.TripId.ShouldBeNull();
        latestLocation.NextStationId.ShouldBeNull();
        latestLocation.RemainingDistanceKmToNextStation.ShouldBeNull();
        latestLocation.RemainingMinutesToNextStation.ShouldBeNull();
        latestLocation.SpeedKmh.ShouldBe(0);
        latestLocation.Status.ShouldBe(IncidentSupport.IncidentLocationStatus);
        latestLocation.ReceivedAt.ShouldBe(now);
        latestLocation.UpdatedAt.ShouldBe(now);
    }

    [Test]
    public async Task AdminCanDispatchRescueBoatForIncidentWithoutPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
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
        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(assignedAt),
            notificationRealtimeNotifier: realtimeNotifier);

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
        savedIncident.RescueDispatchedByUserId.ShouldBe(adminContext.UserId!.Value);
        savedIncident.ReplacementBoatId.ShouldBeNull();
        savedIncident.ReplacementAssignedAt.ShouldBeNull();
        savedIncident.ReplacementAssignedByUserId.ShouldBeNull();
        savedIncident.ReplacementNote.ShouldBe("Dieu tau den ho tro.");
    }

    [Test]
    public async Task CurrentTripWithoutPassengersOrNextTripsCanDispatchOnlyRescueBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var route = Route("R1");
        var currentTrip = Trip(
            route,
            incidentBoat,
            "TR-RESCUE-ONLY",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, currentTrip);
        context.AddRange(incidentBoat, rescueBoat, route, currentTrip, incident);
        await context.SaveChangesAsync();

        var result = await new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7))))
            .Handle(
                new AssignReplacementBoatCommand(
                    incident.Id,
                    rescueBoat.Id,
                    ReplacementBoatId: null,
                    DelayMinutes: 0,
                    Note: "Chỉ điều tàu cứu hộ."),
                CancellationToken.None);

        result.ActiveTicketCount.ShouldBe(0);
        result.OnboardPassengerCount.ShouldBe(0);
        result.FuturePassengerCount.ShouldBe(0);
        result.RescueBoatId.ShouldBe(rescueBoat.Id);
        result.ReplacementBoatId.ShouldBeNull();
        result.ReplacementDelayMinutes.ShouldBe(0);
        context.Trips.Single().BoatId.ShouldBe(incidentBoat.Id);
    }

    [Test]
    public async Task AdminCanAssignManagerToIncident()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB-01");
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau can Manager phu trach.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        context.AddRange(incidentBoat, incident);
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 1, 30, 0, TimeSpan.Zero);
        var handler = new AssignIncidentManagerCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(assignedAt));

        var result = await handler.Handle(
            new AssignIncidentManagerCommand(incident.Id, managerContext.UserId!.Value),
            CancellationToken.None);

        result.AssignedAt.ShouldBe(assignedAt);
        result.AssignedByUserId.ShouldBe(adminContext.UserId!.Value);

        var savedIncident = context.Incidents.Single();
        savedIncident.AssignedManagerId.ShouldBe(managerContext.UserId!.Value);
        savedIncident.AssignedAt.ShouldBe(assignedAt);
        savedIncident.AssignedByUserId.ShouldBe(adminContext.UserId!.Value);
    }

    [Test]
    public async Task AssignedManagerCanDispatchRescueBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var incident = new Incident
        {
            Boat = incidentBoat,
            BoatId = incidentBoat.Id,
            AssignedManagerId = managerContext.UserId!.Value,
            IncidentType = "MechanicalFailure",
            Description = "Tau can cuu ho tren song.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        context.AddRange(incidentBoat, rescueBoat, incident);
        await context.SaveChangesAsync();

        var dispatchedAt = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero);
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(dispatchedAt));

        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: null,
                Note: "Manager duoc gan dieu tau."),
            CancellationToken.None);

        result.RescueBoatId.ShouldBe(rescueBoat.Id);
        result.RescueDispatchedByUserId.ShouldBe(managerContext.UserId!.Value);
        result.RescueDispatchedAt.ShouldBe(dispatchedAt);
    }

    [Test]
    public async Task ManagerCannotDispatchRescueBoat()
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

        var handler = new AssignReplacementBoatCommandHandler(
            context,
            managerContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new AssignReplacementBoatCommand(
                    incident.Id,
                    rescueBoat.Id,
                    ReplacementBoatId: null,
                    DelayMinutes: null,
                    Note: "Manager khong con dieu tau."),
                CancellationToken.None));
    }

    [Test]
    public async Task AssignReplacementBoatPublishesGpsHookWithBoatCodesAndLocation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
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
            adminContext,
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
        notification.TripId.ShouldBeNull();
        notification.TripCode.ShouldBeNull();
        notification.BoatId.ShouldBe(incidentBoat.Id);
        notification.BoatCode.ShouldBe("WB_001");
        notification.RescueBoatId.ShouldBe(rescueBoat.Id);
        notification.RescueBoatCode.ShouldBe("RS_001");
        notification.ReplacementBoatId.ShouldBeNull();
        notification.ReplacementBoatCode.ShouldBeNull();
        notification.Lat.ShouldBe(10.7765m);
        notification.Lng.ShouldBe(106.7065m);
    }

    [Test]
    public async Task GpsMissionReconciliationReturnsAuthoritativeBoatRoles()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var incidentBoat = Boat("WB-FAILED");
        var rescueBoat = RescueBoat("RS-HELP");
        var replacementBoat = Boat("WB-REPLACEMENT");
        var route = Route("R1");
        var trip = Trip(
            route,
            replacementBoat,
            "TR-GPS-MISSION",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        trip.TripStatus = TripStatus.Delayed;
        var resumeAt = new DateTimeOffset(2030, 1, 1, 8, 30, 0, TimeSpan.FromHours(7));
        var incident = Incident(incidentBoat, trip);
        incident.RescueBoat = rescueBoat;
        incident.RescueBoatId = rescueBoat.Id;
        incident.ReplacementBoat = replacementBoat;
        incident.ReplacementBoatId = replacementBoat.Id;
        incident.RescueDispatchedAt = resumeAt.AddMinutes(-20);
        incident.ReplacementAssignedAt = resumeAt.AddMinutes(-20);
        incident.ReplacementMissionType = IncidentReplacementMissionTypes.TransferAtIncidentLocation;
        incident.ReplacementEstimatedResumeAt = resumeAt;
        incident.ReplacementDelayMinutes = 20;
        incident.OnboardPassengerCountSnapshot = 2;
        incident.MissionStatus = IncidentMissionStatuses.ReplacementDispatched;
        context.AddRange(incidentBoat, rescueBoat, replacementBoat, route, trip, incident);
        await context.SaveChangesAsync();

        var now = resumeAt.AddMinutes(-10);
        var handler = new GetGpsIncidentMissionsQueryHandler(context, new FixedTimeProvider(now));
        var rescueResult = await handler.Handle(
            new GetGpsIncidentMissionsQuery(incident.Id, "rs-help"),
            CancellationToken.None);

        rescueResult.ServerTime.ShouldBe(now);
        var rescueMission = rescueResult.Missions.ShouldHaveSingleItem();
        rescueMission.IncidentBoat.BoatId.ShouldBe(incidentBoat.Id);
        rescueMission.IncidentBoat.BoatCode.ShouldBe(incidentBoat.Code);
        rescueMission.RescueBoat.ShouldNotBeNull();
        rescueMission.RescueBoat.BoatId.ShouldBe(rescueBoat.Id);
        rescueMission.ReplacementBoat.ShouldNotBeNull();
        rescueMission.ReplacementBoat.BoatId.ShouldBe(replacementBoat.Id);
        rescueMission.RequestedBoatRole.ShouldBe(IncidentGpsBoatRoles.Rescue);
        rescueMission.RescueNextEvents.ShouldBe([IncidentGpsEventTypes.RescueArrived]);
        rescueMission.ReplacementNextEvents.ShouldBe([IncidentGpsEventTypes.ReplacementArrived]);
        rescueMission.CanReplacementContinueTrip.ShouldBeFalse();
        rescueMission.CurrentOperatingBoatCode.ShouldBe(incidentBoat.Code);

        var replacementResult = await handler.Handle(
            new GetGpsIncidentMissionsQuery(null, "WB-REPLACEMENT"),
            CancellationToken.None);
        replacementResult.Missions.ShouldHaveSingleItem().RequestedBoatRole.ShouldBe(
            IncidentGpsBoatRoles.Replacement);

        var unrelatedResult = await handler.Handle(
            new GetGpsIncidentMissionsQuery(null, "WB-UNKNOWN"),
            CancellationToken.None);
        unrelatedResult.Missions.ShouldBeEmpty();
    }

    [Test]
    public async Task IncidentWithActiveTicketsRequiresPassengerReplacementBoatAndRescueBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
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
            UserId = customerContext.UserId,
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
        var crewAssignment = new StaffWorkAssignment
        {
            StaffUserId = staffContext.UserId!.Value,
            AssignmentType = StaffWorkAssignmentType.Boat,
            BoatId = incidentBoat.Id,
            WorkingDate = trip.OperatingDate,
            StartAt = trip.DepartureTime.AddHours(-1),
            EndAt = trip.ArrivalTime,
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = adminContext.UserId!.Value,
            AssignedAt = trip.DepartureTime.AddDays(-1)
        };
        context.AddRange(
            incidentBoat, rescueBoat, replacementBoat, route, trip, incident, booking, ticket, crewAssignment);
        await context.SaveChangesAsync();

        var assignedAt = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero);
        var realtimeNotifier = new RecordingNotificationRealtimeNotifier();
        var handler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(assignedAt),
            notificationRealtimeNotifier: realtimeNotifier);

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
        savedIncident.MissionStatus.ShouldBe(IncidentMissionStatuses.ReplacementDispatched);

        var savedTrip = context.Trips.Single();
        savedTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedTrip.DelayMinutes.ShouldBe(20);
        savedTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        crewAssignment.BoatId.ShouldBe(replacementBoat.Id);
        crewAssignment.EndAt.ShouldBeGreaterThan(savedTrip.AdjustedArrivalTime!.Value);
        crewAssignment.Note.ShouldNotBeNull().ShouldContain(replacementBoat.Code);

        var tripDetail = await new GetTripDetailQueryHandler(context)
            .Handle(new GetTripDetailQuery(trip.Id), CancellationToken.None);
        tripDetail.Boat.ShouldNotBeNull();
        tripDetail.Boat.VesselId.ShouldBe(replacementBoat.Id);
        tripDetail.IncidentInfo.ShouldNotBeNull();
        tripDetail.IncidentInfo.OriginalBoatId.ShouldBe(incidentBoat.Id);
        tripDetail.IncidentInfo.OriginalBoatCode.ShouldBe(incidentBoat.Code);
        tripDetail.IncidentInfo.RescueBoatId.ShouldBe(rescueBoat.Id);
        tripDetail.IncidentInfo.ReplacementBoatId.ShouldBe(replacementBoat.Id);
        tripDetail.IncidentInfo.IsTripBoatReplaced.ShouldBeTrue();
        tripDetail.IncidentInfo.ReplacementDelayMinutes.ShouldBe(20);

        var delayNotification = context.Set<Notification>()
            .Single(x => x.Type == NotificationTypes.TripDelayed);
        delayNotification.UserId.ShouldBe(customerContext.UserId!.Value);
        delayNotification.Body.ShouldNotBeNull();
        delayNotification.Body.ShouldContain("trễ 20 phút");
        delayNotification.Body.ShouldContain("BK-INCIDENT");

        var incidentNotifications = context.Set<Notification>()
            .Where(x => x.Type == NotificationTypes.IncidentDispatched)
            .ToList();
        incidentNotifications.Count.ShouldBe(3);
        var operationalNotification = incidentNotifications.Single(
            x => x.RelatedEntityType == NotificationRelatedEntityTypes.Incident
                && x.UserId == adminContext.UserId);
        operationalNotification.UserId.ShouldBe(adminContext.UserId!.Value);
        operationalNotification.RelatedEntityId.ShouldBe(incident.Id);
        operationalNotification.Body.ShouldNotBeNull();
        operationalNotification.Body.ShouldContain(rescueBoat.Name);
        operationalNotification.Body.ShouldContain(replacementBoat.Name);
        incidentNotifications.ShouldContain(x =>
            x.RelatedEntityType == NotificationRelatedEntityTypes.Incident
            && x.UserId == staffContext.UserId);

        var customerNotification = incidentNotifications.Single(
            x => x.RelatedEntityType == NotificationRelatedEntityTypes.Booking);
        customerNotification.UserId.ShouldBe(customerContext.UserId!.Value);
        customerNotification.RelatedEntityId.ShouldBe(booking.Id);
        customerNotification.Body.ShouldNotBeNull();
        customerNotification.Body.ShouldContain("BK-INCIDENT");
        realtimeNotifier.Published.Count.ShouldBe(4);
        realtimeNotifier.Published.Select(x => x.NotificationId)
            .ShouldBe(context.Set<Notification>().Select(x => x.Id), ignoreOrder: true);
    }

    [Test]
    public async Task FuturePassengersRequireReplacementBoatToNextBoardingStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
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
        var stationATripStop = TripStop(trip, stationA, 1, null, trip.DepartureTime);
        var stationBTripStop = TripStop(trip, stationB, 2, trip.DepartureTime.AddMinutes(15), trip.DepartureTime.AddMinutes(15));
        var stationCTripStop = TripStop(trip, stationC, 3, trip.DepartureTime.AddMinutes(30), trip.DepartureTime.AddMinutes(30));
        var stationDTripStop = TripStop(trip, stationD, 4, trip.DepartureTime.AddMinutes(45), null);
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
            stationATripStop,
            stationBTripStop,
            stationCTripStop,
            stationDTripStop,
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
            adminContext,
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
        result.ReplacementDelayMinutes.ShouldBe(15);
        result.ReplacementEstimatedResumeAt.ShouldBe(stationCTripStop.PlannedDepartureTime!.Value.AddMinutes(15));

        var savedIncident = context.Incidents.Single();
        savedIncident.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.ContinueFromStation);
        savedIncident.ReplacementTargetStationId.ShouldBe(stationC.Id);
        savedIncident.ReplacementDelayMinutes.ShouldBe(15);
        savedIncident.ReplacementEstimatedResumeAt.ShouldBe(stationCTripStop.PlannedDepartureTime!.Value.AddMinutes(15));
        savedIncident.OnboardPassengerCountSnapshot.ShouldBe(0);
        savedIncident.FuturePassengerCountSnapshot.ShouldBe(1);
        savedIncident.ReplacementNote.ShouldNotBeNull();
        savedIncident.ReplacementNote.ShouldContain("Ben C");

        var savedTrip = context.Trips.Single();
        savedTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedTrip.DelayMinutes.ShouldBe(15);
        savedTrip.AdjustedDepartureTime.ShouldBeNull();
        savedTrip.AdjustedArrivalTime.ShouldBe(stationDTripStop.PlannedArrivalTime!.Value.AddMinutes(15));

        var adjustedStationCStop = context.Set<TripStop>().Single(x => x.Id == stationCTripStop.Id);
        adjustedStationCStop.AdjustedArrivalTime.ShouldBe(stationCTripStop.PlannedArrivalTime!.Value.AddMinutes(15));
        adjustedStationCStop.AdjustedDepartureTime.ShouldBe(stationCTripStop.PlannedDepartureTime!.Value.AddMinutes(15));

        var notification = gpsHook.Notifications.Single();
        notification.TripId.ShouldBe(trip.Id);
        notification.TripCode.ShouldBe(trip.TripCode);
        notification.BoatId.ShouldBe(incidentBoat.Id);
        notification.BoatCode.ShouldBe(incidentBoat.Code);
        notification.RescueBoatId.ShouldBe(rescueBoat.Id);
        notification.RescueBoatCode.ShouldBe(rescueBoat.Code);
        notification.ReplacementBoatId.ShouldBe(replacementBoat.Id);
        notification.ReplacementBoatCode.ShouldBe(replacementBoat.Code);
        notification.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.ContinueFromStation);
        notification.ReplacementTargetStationId.ShouldBe(stationC.Id);
        notification.ReplacementTargetStationName.ShouldBe("Ben C");
        notification.ReplacementDelayMinutes.ShouldBe(15);
        notification.ReplacementEstimatedResumeAt.ShouldBe(stationCTripStop.PlannedDepartureTime!.Value.AddMinutes(15));
        notification.FuturePassengerCount.ShouldBe(1);

        var gpsEventAt = new DateTimeOffset(2030, 1, 1, 8, 35, 0, TimeSpan.FromHours(7));
        var gpsEventHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(gpsEventAt));

        var gpsResult = await gpsEventHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-replacement-arrived-1",
                IncidentGpsEventTypes.ReplacementArrived,
                replacementBoat.Code,
                gpsEventAt,
                Lat: 10.7m,
                Lng: 106.7m,
                StationId: stationC.Id,
                StationCode: stationC.StationCode,
                Note: "Tau thay the da toi ben C.",
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementDispatched,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        gpsResult.Accepted.ShouldBeTrue();
        gpsResult.PreviousMissionStatus.ShouldBe(IncidentMissionStatuses.Dispatched);
        gpsResult.MissionStatus.ShouldBe(IncidentMissionStatuses.ReplacementArrived);
        gpsResult.OperatingStatus.ShouldBe("Dispatched");
        gpsResult.CanReplacementContinueTrip.ShouldBeFalse();
        gpsResult.CurrentOperatingBoatCode.ShouldBe(incidentBoat.Code);
        gpsResult.ReplacementEstimatedResumeAt.ShouldBe(
            stationCTripStop.PlannedDepartureTime!.Value.AddMinutes(15));

        var waitingTrip = context.Trips.Single();
        waitingTrip.BoatId.ShouldBe(replacementBoat.Id);
        waitingTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        var waitingIncident = context.Incidents.Single();
        waitingIncident.ReplacementArrivedAt.ShouldBe(gpsEventAt);
        waitingIncident.MissionStatus.ShouldBe(IncidentMissionStatuses.ReplacementArrived);
        context.IncidentMissionEvents.ShouldHaveSingleItem().GpsEventId.ShouldBe("gps-replacement-arrived-1");

        var resumeHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(stationCTripStop.PlannedDepartureTime!.Value.AddMinutes(15)));
        var replayResult = await resumeHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-replacement-arrived-1",
                IncidentGpsEventTypes.ReplacementArrived,
                replacementBoat.Code,
                gpsEventAt,
                Lat: 10.7m,
                Lng: 106.7m,
                StationId: stationC.Id,
                StationCode: stationC.StationCode,
                Note: "Tau thay the da toi ben C.",
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementDispatched,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        replayResult.CanReplacementContinueTrip.ShouldBeTrue();
        replayResult.CurrentOperatingBoatCode.ShouldBe(replacementBoat.Code);
        context.Trips.Single().TripStatus.ShouldBe(TripStatus.InProgress);
        context.IncidentMissionEvents.Count().ShouldBe(1);
    }

    [Test]
    public async Task GpsEventRequiresPassengerTransferBeforeTowingWhenPassengersAreOnboard()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
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
            TripCode = "TR-ONBOARD",
            OperatingDate = new DateOnly(2030, 1, 1),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)),
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
            Description = "Tau hong khi co khach tren tau.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
        };
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-ONBOARD",
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
            FullName = "Khach tren tau"
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = "TICKET-ONBOARD",
            QrToken = "QR-ONBOARD",
            TicketStatus = TicketStatus.CheckedIn,
            CheckedInAt = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)),
            IssuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        context.AddRange(incidentBoat, rescueBoat, replacementBoat, route, trip, incident, booking, passenger, ticket);
        await context.SaveChangesAsync();

        var assignHandler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 15, 0, TimeSpan.FromHours(7))));

        var assignResult = await assignHandler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                replacementBoat.Id,
                DelayMinutes: 20,
                Note: null),
            CancellationToken.None);

        assignResult.OnboardPassengerCount.ShouldBe(1);
        assignResult.FuturePassengerCount.ShouldBe(0);
        context.Trips.Single().BoatId.ShouldBe(replacementBoat.Id);

        var gpsHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 20, 0, TimeSpan.FromHours(7))));
        var rescueArrivedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 0, TimeSpan.FromHours(7));
        var rescueArrivedResult = await gpsHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-rescue-arrived-onboard",
                IncidentGpsEventTypes.RescueArrived,
                rescueBoat.Code,
                rescueArrivedAt,
                Lat: 10.7m,
                Lng: 106.7m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementDispatched,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        rescueArrivedResult.CanReplacementContinueTrip.ShouldBeFalse();
        rescueArrivedResult.CurrentOperatingBoatCode.ShouldBe(incidentBoat.Code);
        rescueArrivedResult.ExpectedBoatRole.ShouldBe(IncidentGpsBoatRoles.Rescue);
        rescueArrivedResult.IncidentBoatCode.ShouldBe(incidentBoat.Code);
        rescueArrivedResult.RescueBoatCode.ShouldBe(rescueBoat.Code);
        rescueArrivedResult.ReplacementBoatCode.ShouldBe(replacementBoat.Code);

        await Should.ThrowAsync<ConflictException>(() =>
            gpsHandler.Handle(
                new RecordIncidentGpsEventCommand(
                    incident.Id,
                    "gps-towing-too-early",
                    IncidentGpsEventTypes.TowingStarted,
                    rescueBoat.Code,
                    rescueArrivedAt.AddMinutes(1),
                    Lat: 10.7m,
                    Lng: 106.7m,
                    StationId: null,
                    StationCode: null,
                    Note: null,
                    PreviousMissionStatus: IncidentMissionStatuses.RescueArrived,
                    EstimatedTowingMinutes: 45),
                CancellationToken.None));

        await gpsHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-replacement-arrived-onboard",
                IncidentGpsEventTypes.ReplacementArrived,
                replacementBoat.Code,
                rescueArrivedAt.AddMinutes(5),
                Lat: 10.7001m,
                Lng: 106.7001m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.RescueArrived,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        await Should.ThrowAsync<ConflictException>(() =>
            gpsHandler.Handle(
                new RecordIncidentGpsEventCommand(
                    incident.Id,
                    "gps-transfer-wrong-boat-role",
                    IncidentGpsEventTypes.PassengerTransferCompleted,
                    rescueBoat.Code,
                    rescueArrivedAt.AddMinutes(6),
                    Lat: 10.7001m,
                    Lng: 106.7001m,
                    StationId: null,
                    StationCode: null,
                    Note: null,
                    PreviousMissionStatus: IncidentMissionStatuses.ReplacementArrived,
                    EstimatedTowingMinutes: null),
                CancellationToken.None));
        context.IncidentMissionEvents.ShouldNotContain(
            x => x.GpsEventId == "gps-transfer-wrong-boat-role");

        var transferResult = await gpsHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-transfer-completed-onboard",
                IncidentGpsEventTypes.PassengerTransferCompleted,
                replacementBoat.Code,
                rescueArrivedAt.AddMinutes(7),
                Lat: 10.7001m,
                Lng: 106.7001m,
                StationId: null,
                StationCode: null,
                Note: "Da chuyen khach sang tau thay the.",
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementArrived,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        transferResult.MissionStatus.ShouldBe(IncidentMissionStatuses.PassengerTransferCompleted);
        transferResult.CanReplacementContinueTrip.ShouldBeFalse();
        transferResult.CanRescueStartTowing.ShouldBeTrue();
        transferResult.ExpectedBoatRole.ShouldBe(IncidentGpsBoatRoles.Replacement);
        context.Trips.Single().BoatId.ShouldBe(replacementBoat.Id);
        context.Trips.Single().TripStatus.ShouldBe(TripStatus.Delayed);

        var resumeHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 35, 0, TimeSpan.FromHours(7))));
        var resumeResult = await resumeHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-transfer-completed-onboard",
                IncidentGpsEventTypes.PassengerTransferCompleted,
                replacementBoat.Code,
                rescueArrivedAt.AddMinutes(7),
                Lat: 10.7001m,
                Lng: 106.7001m,
                StationId: null,
                StationCode: null,
                Note: "Da chuyen khach sang tau thay the.",
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementArrived,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        resumeResult.CanReplacementContinueTrip.ShouldBeTrue();
        resumeResult.CurrentOperatingBoatCode.ShouldBe(replacementBoat.Code);
        context.Trips.Single().TripStatus.ShouldBe(TripStatus.InProgress);

        var towingStartedAt = rescueArrivedAt.AddMinutes(20);
        var towingHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(towingStartedAt));
        var towingResult = await towingHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-towing-started-onboard",
                IncidentGpsEventTypes.TowingStarted,
                rescueBoat.Code,
                towingStartedAt,
                Lat: 10.7m,
                Lng: 106.7m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.PassengerTransferCompleted,
                EstimatedTowingMinutes: 45),
            CancellationToken.None);

        towingResult.MissionStatus.ShouldBe(IncidentMissionStatuses.TowingStarted);
        towingResult.CanRescueStartTowing.ShouldBeFalse();
        var savedIncident = context.Incidents.Single();
        savedIncident.PassengerTransferCompletedAt.ShouldBe(rescueArrivedAt.AddMinutes(7));
        savedIncident.TowingStartedAt.ShouldBe(towingStartedAt);
        savedIncident.EstimatedTowingMinutes.ShouldBe(45);
    }

    [Test]
    public async Task LatePassengerTransferIncreasesDelayAndCascadesToNextTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var replacementBoat = Boat("WB-02");
        var route = Route("R1");
        var trip = Trip(
            route,
            incidentBoat,
            "TR-LATE-CURRENT",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        trip.TripStatus = TripStatus.Delayed;
        var nextTrip = Trip(
            route,
            incidentBoat,
            "TR-LATE-NEXT",
            new DateTimeOffset(2030, 1, 1, 8, 55, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, trip);
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = "BK-LATE",
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
            FullName = "Khach tren tau"
        };
        var ticket = new Ticket
        {
            Booking = booking,
            BookingPassenger = passenger,
            TicketCode = "TICKET-LATE",
            QrToken = "QR-LATE",
            TicketStatus = TicketStatus.CheckedIn,
            CheckedInAt = trip.DepartureTime,
            IssuedAt = trip.DepartureTime.AddHours(-1)
        };
        context.AddRange(
            incidentBoat,
            rescueBoat,
            replacementBoat,
            route,
            trip,
            nextTrip,
            incident,
            booking,
            passenger,
            ticket);
        await context.SaveChangesAsync();

        var assignHandler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7))));
        var assignResult = await assignHandler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                replacementBoat.Id,
                DelayMinutes: 20,
                Note: null),
            CancellationToken.None);

        assignResult.ReplacementEstimatedResumeAt.ShouldBe(
            new DateTimeOffset(2030, 1, 1, 8, 30, 0, TimeSpan.FromHours(7)));
        context.Trips.Single(x => x.Id == nextTrip.Id).DelayMinutes.ShouldBe(0);

        var arrivedAt = new DateTimeOffset(2030, 1, 1, 8, 20, 0, TimeSpan.FromHours(7));
        var arrivedHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(arrivedAt));
        await arrivedHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-late-replacement-arrived",
                IncidentGpsEventTypes.ReplacementArrived,
                replacementBoat.Code,
                arrivedAt,
                Lat: 10.7m,
                Lng: 106.7m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementDispatched,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        var transferCompletedAt = new DateTimeOffset(2030, 1, 1, 8, 40, 0, TimeSpan.FromHours(7));
        var transferHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(transferCompletedAt));
        var result = await transferHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-late-transfer-completed",
                IncidentGpsEventTypes.PassengerTransferCompleted,
                replacementBoat.Code,
                transferCompletedAt,
                Lat: 10.7m,
                Lng: 106.7m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.ReplacementArrived,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        result.CanReplacementContinueTrip.ShouldBeTrue();
        result.ReplacementEstimatedResumeAt.ShouldBe(transferCompletedAt);

        var savedIncident = context.Incidents.Single();
        savedIncident.ReplacementDelayMinutes.ShouldBe(30);
        savedIncident.ReplacementEstimatedResumeAt.ShouldBe(transferCompletedAt);

        var savedCurrentTrip = context.Trips.Single(x => x.Id == trip.Id);
        savedCurrentTrip.DelayMinutes.ShouldBe(30);
        savedCurrentTrip.AdjustedArrivalTime.ShouldBe(trip.ArrivalTime.AddMinutes(30));
        savedCurrentTrip.DelayPropagationMinutes.ShouldBe(10);
        savedCurrentTrip.TripStatus.ShouldBe(TripStatus.InProgress);

        var savedNextTrip = context.Trips.Single(x => x.Id == nextTrip.Id);
        savedNextTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedNextTrip.DelayMinutes.ShouldBe(10);
        savedNextTrip.AdjustedDepartureTime.ShouldBe(nextTrip.DepartureTime.AddMinutes(10));
        savedNextTrip.AdjustedArrivalTime.ShouldBe(nextTrip.ArrivalTime.AddMinutes(10));
        savedNextTrip.TripStatus.ShouldBe(TripStatus.Delayed);
    }

    [Test]
    public async Task TripWithNoRemainingPassengersCanDispatchOnlyRescueBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
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
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: 0,
                Note: null),
            CancellationToken.None);

        result.ActiveTicketCount.ShouldBe(1);
        result.OnboardPassengerCount.ShouldBe(0);
        result.FuturePassengerCount.ShouldBe(0);
        result.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.None);
        result.ReplacementTargetStationId.ShouldBeNull();
        result.ReplacementBoatId.ShouldBeNull();
        result.RescueBoatId.ShouldBe(rescueBoat.Id);

        var savedIncident = context.Incidents.Single();
        savedIncident.RescueBoatId.ShouldBe(rescueBoat.Id);
        savedIncident.ReplacementBoatId.ShouldBeNull();
        savedIncident.ReplacementMissionType.ShouldBe(IncidentReplacementMissionTypes.None);
        savedIncident.ReplacementTargetStationId.ShouldBeNull();

        var savedTrip = context.Trips.Single();
        savedTrip.BoatId.ShouldBe(incidentBoat.Id);
        savedTrip.DelayMinutes.ShouldBe(0);
        savedTrip.AdjustedDepartureTime.ShouldBeNull();
        savedTrip.AdjustedArrivalTime.ShouldBeNull();
        savedTrip.TripStatus.ShouldBe(TripStatus.InProgress);

        var gpsHandler = new RecordIncidentGpsEventCommandHandler(
            context,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 25, 0, TimeSpan.FromHours(7))));
        await gpsHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-rescue-arrived-no-passengers",
                IncidentGpsEventTypes.RescueArrived,
                rescueBoat.Code,
                new DateTimeOffset(2030, 1, 1, 8, 25, 0, TimeSpan.FromHours(7)),
                Lat: 10.7765m,
                Lng: 106.7065m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.RescueDispatched,
                EstimatedTowingMinutes: null),
            CancellationToken.None);
        await gpsHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-towing-started-no-passengers",
                IncidentGpsEventTypes.TowingStarted,
                rescueBoat.Code,
                new DateTimeOffset(2030, 1, 1, 8, 26, 0, TimeSpan.FromHours(7)),
                Lat: 10.7765m,
                Lng: 106.7065m,
                StationId: null,
                StationCode: null,
                Note: null,
                PreviousMissionStatus: IncidentMissionStatuses.RescueArrived,
                EstimatedTowingMinutes: 30),
            CancellationToken.None);
        await gpsHandler.Handle(
            new RecordIncidentGpsEventCommand(
                incident.Id,
                "gps-towing-completed-no-passengers",
                IncidentGpsEventTypes.TowingCompleted,
                rescueBoat.Code,
                new DateTimeOffset(2030, 1, 1, 8, 56, 0, TimeSpan.FromHours(7)),
                Lat: 10.7765m,
                Lng: 106.7065m,
                StationId: null,
                StationCode: null,
                Note: "Tau loi da ve ben.",
                PreviousMissionStatus: IncidentMissionStatuses.TowingStarted,
                EstimatedTowingMinutes: null),
            CancellationToken.None);

        var maintainedBoat = context.Boats.Single(x => x.Id == incidentBoat.Id);
        maintainedBoat.Status.ShouldBe(BoatStatus.UnderMaintenance);
        var cancelledTrip = context.Trips.Single();
        cancelledTrip.TripStatus.ShouldBe(TripStatus.Cancelled);
        cancelledTrip.StatusNote.ShouldNotBeNull();
        cancelledTrip.StatusNote.ShouldContain("bảo trì");
    }

    [Test]
    public async Task DelayAppliesToCurrentTripAndOnlyCascadesWhenTurnaroundRequiresIt()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var replacementBoat = Boat("WB-02");
        var route = Route("R1");
        var trip = Trip(route, incidentBoat, "TR-1", new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var futureTrip = Trip(route, incidentBoat, "TR-2", new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, trip);
        context.AddRange(incidentBoat, rescueBoat, replacementBoat, route, trip, futureTrip, incident);
        await context.SaveChangesAsync();

        var handler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7))));

        await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                replacementBoat.Id,
                DelayMinutes: 14,
                Note: "Delay các chuyến kế tiếp."),
            CancellationToken.None);

        var savedCurrentTrip = context.Trips.Single(x => x.Id == trip.Id);
        savedCurrentTrip.BoatId.ShouldBe(incidentBoat.Id);
        savedCurrentTrip.DelayMinutes.ShouldBe(14);
        savedCurrentTrip.TripStatus.ShouldBe(TripStatus.Delayed);

        var savedFutureTrip = context.Trips.Single(x => x.Id == futureTrip.Id);
        savedFutureTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedFutureTrip.DelayMinutes.ShouldBe(0);
        savedFutureTrip.AdjustedDepartureTime.ShouldBeNull();
        savedFutureTrip.AdjustedArrivalTime.ShouldBeNull();
        savedFutureTrip.TripStatus.ShouldBe(TripStatus.Scheduled);
    }

    [Test]
    public async Task DelayCascadesAcrossRoutesUsingArrivalAndTurnaroundBuffer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var otherBoat = Boat("WB-99");
        var rescueBoat = RescueBoat("RS-01");
        var replacementBoat = Boat("WB-02");
        var route = Route("R1");
        var otherRoute = Route("R2");
        var trip = Trip(route, incidentBoat, "TR-1", new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var futureTrip = Trip(route, incidentBoat, "TR-2", new DateTimeOffset(2030, 1, 1, 8, 45, 0, TimeSpan.FromHours(7)));
        var sameBoatOtherRouteTrip = Trip(otherRoute, incidentBoat, "TR-OTHER-ROUTE", new DateTimeOffset(2030, 1, 1, 9, 15, 0, TimeSpan.FromHours(7)));
        var sameRouteOtherBoatTrip = Trip(route, otherBoat, "TR-OTHER-BOAT", new DateTimeOffset(2030, 1, 1, 9, 30, 0, TimeSpan.FromHours(7)));
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var futureStopA = TripStop(futureTrip, stationA, 1, null, futureTrip.DepartureTime);
        var futureStopB = TripStop(futureTrip, stationB, 2, futureTrip.ArrivalTime, null);
        var incident = Incident(incidentBoat, trip);
        context.AddRange(
            incidentBoat,
            otherBoat,
            rescueBoat,
            replacementBoat,
            route,
            otherRoute,
            stationA,
            stationB,
            trip,
            futureTrip,
            sameBoatOtherRouteTrip,
            sameRouteOtherBoatTrip,
            futureStopA,
            futureStopB,
            incident);
        await context.SaveChangesAsync();

        var handler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7))));

        await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                replacementBoat.Id,
                DelayMinutes: 15,
                Note: "Trễ vượt ngưỡng, ảnh hưởng chuyến sau."),
            CancellationToken.None);

        var savedFutureTrip = context.Trips.Single(x => x.Id == futureTrip.Id);
        savedFutureTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedFutureTrip.DelayMinutes.ShouldBe(5);
        savedFutureTrip.AdjustedDepartureTime.ShouldBe(futureTrip.DepartureTime.AddMinutes(5));
        savedFutureTrip.AdjustedArrivalTime.ShouldBe(futureTrip.ArrivalTime.AddMinutes(5));
        savedFutureTrip.TripStatus.ShouldBe(TripStatus.Delayed);

        var savedFutureStopA = context.Set<TripStop>().Single(x => x.Id == futureStopA.Id);
        savedFutureStopA.AdjustedDepartureTime.ShouldBe(futureStopA.PlannedDepartureTime!.Value.AddMinutes(5));
        var savedFutureStopB = context.Set<TripStop>().Single(x => x.Id == futureStopB.Id);
        savedFutureStopB.AdjustedArrivalTime.ShouldBe(futureStopB.PlannedArrivalTime!.Value.AddMinutes(5));

        var savedSameBoatOtherRouteTrip = context.Trips.Single(x => x.Id == sameBoatOtherRouteTrip.Id);
        savedSameBoatOtherRouteTrip.BoatId.ShouldBe(replacementBoat.Id);
        savedSameBoatOtherRouteTrip.DelayMinutes.ShouldBe(10);
        savedSameBoatOtherRouteTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        savedSameBoatOtherRouteTrip.AdjustedDepartureTime.ShouldBe(sameBoatOtherRouteTrip.DepartureTime.AddMinutes(10));
        savedSameBoatOtherRouteTrip.AdjustedArrivalTime.ShouldBe(sameBoatOtherRouteTrip.ArrivalTime.AddMinutes(10));

        var savedSameRouteOtherBoatTrip = context.Trips.Single(x => x.Id == sameRouteOtherBoatTrip.Id);
        savedSameRouteOtherBoatTrip.DelayMinutes.ShouldBe(0);
        savedSameRouteOtherBoatTrip.TripStatus.ShouldBe(TripStatus.Scheduled);
        savedSameRouteOtherBoatTrip.AdjustedDepartureTime.ShouldBeNull();
    }

    [Test]
    public async Task NextTripsRequireReplacementOrPositiveDelayEvenWithoutPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var route = Route("R1");
        var currentTrip = Trip(route, incidentBoat, "TR-1", new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var nextTrip = Trip(route, incidentBoat, "TR-2", new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, currentTrip);
        context.AddRange(incidentBoat, rescueBoat, route, currentTrip, nextTrip, incident);
        await context.SaveChangesAsync();

        var handler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7))));

        var exception = await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: null,
                Note: null),
            CancellationToken.None));

        exception.Errors["delayMinutes"].Single().ShouldContain("1 chuyến kế tiếp");
    }

    [Test]
    public async Task NoReplacementBoatDelayUsesExistingScheduleGapBeforeCascading()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var rescueBoat = RescueBoat("RS-01");
        var route = Route("R1");
        var currentTrip = Trip(route, incidentBoat, "TR-1", new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var nextTrip = Trip(route, incidentBoat, "TR-2", new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, currentTrip);
        context.AddRange(incidentBoat, rescueBoat, route, currentTrip, nextTrip, incident);
        await context.SaveChangesAsync();

        var handler = new AssignReplacementBoatCommandHandler(
            context,
            adminContext,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 8, 10, 0, TimeSpan.FromHours(7))));
        var result = await handler.Handle(
            new AssignReplacementBoatCommand(
                incident.Id,
                rescueBoat.Id,
                ReplacementBoatId: null,
                DelayMinutes: 25,
                Note: "Chưa có tàu thay thế."),
            CancellationToken.None);

        result.ReplacementBoatId.ShouldBeNull();
        result.ReplacementDelayMinutes.ShouldBe(25);
        var savedCurrentTrip = context.Trips.Single(x => x.Id == currentTrip.Id);
        savedCurrentTrip.DelayMinutes.ShouldBe(25);
        savedCurrentTrip.TripStatus.ShouldBe(TripStatus.Delayed);
        var savedNextTrip = context.Trips.Single(x => x.Id == nextTrip.Id);
        savedNextTrip.BoatId.ShouldBe(incidentBoat.Id);
        savedNextTrip.DelayMinutes.ShouldBe(0);
        savedNextTrip.TripStatus.ShouldBe(TripStatus.Scheduled);
        savedNextTrip.AdjustedDepartureTime.ShouldBeNull();
    }

    [Test]
    public async Task DispatchPlanShowsNextTripsEvenWhenTheyHaveNoPassengers()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var route = Route("R1");
        var currentTrip = Trip(route, incidentBoat, "TR-1", new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var nextTrip = Trip(route, incidentBoat, "TR-2", new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)));
        var tomorrowTrip = Trip(route, incidentBoat, "TR-TOMORROW", new DateTimeOffset(2030, 1, 2, 9, 0, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, currentTrip);
        context.AddRange(incidentBoat, route, currentTrip, nextTrip, tomorrowTrip, incident);
        await context.SaveChangesAsync();

        var result = await new GetIncidentDispatchPlanQueryHandler(context, adminContext).Handle(
            new GetIncidentDispatchPlanQuery(incident.Id),
            CancellationToken.None);

        result.ActiveTicketCount.ShouldBe(0);
        result.RequiresPassengerReplacement.ShouldBeFalse();
        result.HasNextTrips.ShouldBeTrue();
        result.NextTripCount.ShouldBe(2);
        result.RequiredReplacementSeatCount.ShouldBe(20);
        result.RequiresReplacementOrDelay.ShouldBeTrue();
        result.NextTrips.Select(x => x.TripCode).ShouldBe(["TR-2", "TR-TOMORROW"]);
    }

    [Test]
    public async Task AvailableReplacementBoatsExcludeInsufficientAndScheduleConflictingBoats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var incidentBoat = Boat("WB-01");
        var eligibleBoat = Boat("WB-ELIGIBLE");
        var insufficientBoat = Boat("WB-SMALL");
        insufficientBoat.SeatCount = 10;
        var busyBoat = Boat("WB-BUSY");
        var route = Route("R1");
        var currentTrip = Trip(route, incidentBoat, "TR-1", new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        var nextTrip = Trip(route, incidentBoat, "TR-2", new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.FromHours(7)));
        var conflictingTrip = Trip(route, busyBoat, "TR-BUSY", new DateTimeOffset(2030, 1, 1, 9, 10, 0, TimeSpan.FromHours(7)));
        var incident = Incident(incidentBoat, currentTrip);
        context.AddRange(
            incidentBoat,
            eligibleBoat,
            insufficientBoat,
            busyBoat,
            route,
            currentTrip,
            nextTrip,
            conflictingTrip,
            incident);
        await context.SaveChangesAsync();

        var result = await new GetAvailableReplacementBoatsQueryHandler(context, adminContext).Handle(
            new GetAvailableReplacementBoatsQuery(incident.Id),
            CancellationToken.None);

        result.ShouldHaveSingleItem().Id.ShouldBe(eligibleBoat.Id);
    }

    [Test]
    public async Task SightseeingLoopWithRepeatedStationLoadsDispatchPlanAndReplacementBoats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var loopStation = Station("BD", "Bến Bạch Đằng");
        var route = Route("LOOP-BD");
        route.RouteType = RouteTypes.SightseeingLoop;
        route.RouteStops.Add(RouteStop(route, loopStation, 1));
        route.RouteStops.Add(RouteStop(route, loopStation, 2));
        var incidentBoat = Boat("WS-001");
        var replacementBoat = Boat("WB-REPLACEMENT");
        var currentTrip = Trip(
            route,
            incidentBoat,
            "BS-LOOP-BD-0800",
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));
        currentTrip.TripStatus = TripStatus.InProgress;
        var incident = Incident(incidentBoat, currentTrip);
        var gpsTime = currentTrip.DepartureTime.AddMinutes(10);
        var latestLocation = new BoatLatestLocation
        {
            BoatId = incidentBoat.Id,
            GpsDeviceId = Guid.NewGuid(),
            TripId = currentTrip.Id,
            NextStationId = loopStation.Id,
            Latitude = 10.7000000m,
            Longitude = 106.7000000m,
            RecordedAt = gpsTime,
            ReceivedAt = gpsTime,
            UpdatedAt = gpsTime
        };
        context.AddRange(
            loopStation,
            route,
            incidentBoat,
            replacementBoat,
            currentTrip,
            incident,
            latestLocation);
        await context.SaveChangesAsync();

        var dispatchPlan = await new GetIncidentDispatchPlanQueryHandler(context, adminContext).Handle(
            new GetIncidentDispatchPlanQuery(incident.Id),
            CancellationToken.None);
        var replacementBoats = await new GetAvailableReplacementBoatsQueryHandler(context, adminContext).Handle(
            new GetAvailableReplacementBoatsQuery(incident.Id),
            CancellationToken.None);

        dispatchPlan.IncidentId.ShouldBe(incident.Id);
        dispatchPlan.ActiveTicketCount.ShouldBe(0);
        replacementBoats.Select(x => x.Id).ShouldContain(replacementBoat.Id);
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

    private static Trip Trip(Route route, Boat boat, string tripCode, DateTimeOffset departureTime) =>
        new()
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            OperatingDate = DateOnly.FromDateTime(departureTime.DateTime),
            DepartureTime = departureTime,
            ArrivalTime = departureTime.AddMinutes(30),
            CapacitySnapshot = 20,
            TripStatus = TripStatus.Scheduled
        };

    private static Incident Incident(Boat boat, Trip trip) =>
        new()
        {
            Boat = boat,
            BoatId = boat.Id,
            Trip = trip,
            TripId = trip.Id,
            IncidentType = "MechanicalFailure",
            Description = "Tau can cuu ho tren song.",
            Severity = "High",
            OccurredAt = new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ResolutionStatus = IncidentSupport.OpenStatus
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

    private static TripStop TripStop(
        Trip trip,
        Station station,
        int stopOrder,
        DateTimeOffset? plannedArrival,
        DateTimeOffset? plannedDeparture) =>
        new()
        {
            Trip = trip,
            TripId = trip.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            PlannedArrivalTime = plannedArrival,
            PlannedDepartureTime = plannedDeparture
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
