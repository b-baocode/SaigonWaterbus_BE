using System.Globalization;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Notifications;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Incidents;

internal static class IncidentDelayNotificationSupport
{
    public static async Task<IReadOnlyList<Notification>> AddAsync(
        IApplicationDbContext context,
        Trip trip,
        int changedDelayMinutes,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (changedDelayMinutes <= 0)
        {
            return [];
        }

        var recipients = await LoadRecipientsAsync(context, trip, cancellationToken);
        if (recipients.Count == 0)
        {
            return [];
        }

        var notifications = new List<Notification>(recipients.Count);
        foreach (var recipient in recipients)
        {
            var notification = new Notification
            {
                UserId = recipient.UserId,
                Title = "Chuyến đi bị trễ",
                Body = $"{body} Vui lòng theo dõi giờ rời bến mới trên vé."
                    + $"{FormatExpectedDeparture(recipient.ExpectedBoardingDeparture)} "
                    + $"Booking {recipient.BookingCode} bị ảnh hưởng.",
                Type = NotificationTypes.TripDelayed,
                RelatedEntityType = NotificationRelatedEntityTypes.Booking,
                RelatedEntityId = recipient.BookingId,
                CreatedAt = now
            };
            context.Set<Notification>().Add(notification);
            notifications.Add(notification);
        }

        return notifications;
    }

    private static async Task<IReadOnlyList<Recipient>> LoadRecipientsAsync(
        IApplicationDbContext context,
        Trip trip,
        CancellationToken cancellationToken)
    {
        var passengerCandidates = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => x.Booking.UserId != null
                && x.Booking.BookingStatus == BookingStatus.Confirmed
                && (x.TripId == trip.Id
                    || (!x.TripId.HasValue && x.Booking.TripId == trip.Id)
                    || (x.TripSeat != null && x.TripSeat.TripId == trip.Id)))
            .Select(x => new Candidate(
                x.BookingId,
                x.Booking.UserId!.Value,
                x.Booking.BookingCode,
                x.FromStopOrder))
            .ToListAsync(cancellationToken);

        var sourceBookingId = trip.SourceBookingId;
        var directBookingCandidates = await context.Set<Booking>()
            .AsNoTracking()
            .Where(x => x.UserId != null
                && x.BookingStatus == BookingStatus.Confirmed
                && (x.TripId == trip.Id
                    || x.ReturnTripId == trip.Id
                    || (sourceBookingId.HasValue && x.Id == sourceBookingId.Value)))
            .Select(x => new Candidate(
                x.Id,
                x.UserId!.Value,
                x.BookingCode,
                null))
            .ToListAsync(cancellationToken);

        return passengerCandidates
            .Concat(directBookingCandidates)
            .GroupBy(x => x.BookingId)
            .Select(group =>
            {
                var first = group.First();
                return new Recipient(
                    first.BookingId,
                    first.UserId,
                    first.BookingCode,
                    ResolveBoardingDeparture(trip, first.FromStopOrder));
            })
            .ToArray();
    }

    private static DateTimeOffset? ResolveBoardingDeparture(Trip trip, int? fromStopOrder)
    {
        if (trip.TripStops.Count == 0)
        {
            return trip.AdjustedDepartureTime ?? trip.DepartureTime;
        }

        var stop = fromStopOrder.HasValue
            ? trip.TripStops.FirstOrDefault(x => x.StopOrder == fromStopOrder.Value)
            : trip.TripStops.OrderBy(x => x.StopOrder).FirstOrDefault();
        return stop?.AdjustedDepartureTime
            ?? stop?.PlannedDepartureTime
            ?? stop?.AdjustedArrivalTime
            ?? stop?.PlannedArrivalTime
            ?? trip.AdjustedDepartureTime
            ?? trip.DepartureTime;
    }

    private static string FormatExpectedDeparture(DateTimeOffset? expectedDeparture)
    {
        if (!expectedDeparture.HasValue)
        {
            return string.Empty;
        }

        var vietnamTime = expectedDeparture.Value.ToOffset(TimeSpan.FromHours(7));
        return $" Giờ rời bến dự kiến: {vietnamTime.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture)}.";
    }

    private sealed record Candidate(
        Guid BookingId,
        Guid UserId,
        string BookingCode,
        int? FromStopOrder);

    private sealed record Recipient(
        Guid BookingId,
        Guid UserId,
        string BookingCode,
        DateTimeOffset? ExpectedBoardingDeparture);
}
