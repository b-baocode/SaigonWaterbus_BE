using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Bookings;

internal static class LapInfantTicketSupport
{
    private const string InfantTicketTypeCode = "INFANT";
    private const string ChildTicketTypeCode = "CHILD";
    private const string AdultTicketTypeCode = "ADULT";

    public static bool IsLapInfant(BookingPassenger passenger) =>
        string.Equals(passenger.PassengerType?.Trim(), InfantTicketTypeCode, StringComparison.OrdinalIgnoreCase)
        && passenger.TripId.HasValue
        && !passenger.TripSeatId.HasValue
        && passenger.TripSeat is null;

    public static bool IsAccompaniedChild(BookingPassenger passenger) =>
        string.Equals(passenger.PassengerType?.Trim(), ChildTicketTypeCode, StringComparison.OrdinalIgnoreCase)
        && passenger.TripId.HasValue;

    public static bool UsesCompanionTicket(BookingPassenger passenger) =>
        IsLapInfant(passenger) || IsAccompaniedChild(passenger);

    public static bool RequiresOwnTicket(BookingPassenger passenger) =>
        !UsesCompanionTicket(passenger);

    public static IReadOnlyDictionary<Guid, Guid> AssignInfantsToCompanions(
        IEnumerable<BookingPassenger> passengers) =>
        AssignCompanionTicketPassengersToAdults(passengers);

    public static IReadOnlyDictionary<Guid, Guid> AssignCompanionTicketPassengersToAdults(
        IEnumerable<BookingPassenger> passengers)
    {
        var companions = passengers
            .Where(IsEligibleCompanion)
            .OrderBy(PassengerLegOrder)
            .ThenBy(x => x.FromStopOrder ?? int.MaxValue)
            .ThenBy(x => x.ToStopOrder ?? int.MaxValue)
            .ThenBy(x => x.TripSeat?.Seat?.Code)
            .ThenBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToList();
        var usedCompanionIds = new HashSet<Guid>();
        var assignmentByPassengerId = new Dictionary<Guid, Guid>();

        foreach (var dependent in passengers
                     .Where(UsesCompanionTicket)
                     .OrderBy(PassengerLegOrder)
                     .ThenBy(x => x.FromStopOrder ?? int.MaxValue)
                     .ThenBy(x => x.ToStopOrder ?? int.MaxValue)
                     .ThenBy(x => x.FullName)
                     .ThenBy(x => x.Id))
        {
            var companion = companions.FirstOrDefault(candidate =>
                !usedCompanionIds.Contains(candidate.Id)
                && SameTicketSegment(candidate, dependent));
            if (companion is null)
            {
                continue;
            }

            assignmentByPassengerId[dependent.Id] = companion.Id;
            usedCompanionIds.Add(companion.Id);
        }

        return assignmentByPassengerId;
    }

    public static IReadOnlyList<BookingPassenger> ResolvePassengersRepresentedByTicket(
        IEnumerable<BookingPassenger> passengers,
        BookingPassenger? ticketPassenger)
    {
        if (ticketPassenger is null)
        {
            return passengers
                .OrderBy(PassengerLegOrder)
                .ThenBy(x => x.TripSeat?.Seat?.Code)
                .ThenBy(x => x.FullName)
                .ToArray();
        }

        if (UsesCompanionTicket(ticketPassenger))
        {
            return [ticketPassenger];
        }

        var assignments = AssignCompanionTicketPassengersToAdults(passengers);
        var represented = passengers
            .Where(passenger => passenger.Id == ticketPassenger.Id
                || (assignments.TryGetValue(passenger.Id, out var companionId)
                    && companionId == ticketPassenger.Id))
            .OrderBy(x => x.Id == ticketPassenger.Id ? 0 : 1)
            .ThenBy(x => x.FullName)
            .ToArray();

        return represented.Length == 0 ? [ticketPassenger] : represented;
    }

    private static bool IsEligibleCompanion(BookingPassenger passenger) =>
        string.Equals(passenger.PassengerType?.Trim(), AdultTicketTypeCode, StringComparison.OrdinalIgnoreCase)
        && (passenger.TripSeatId.HasValue || passenger.TripSeat is not null);

    private static bool SameTicketSegment(BookingPassenger companion, BookingPassenger infant) =>
        companion.TripId == infant.TripId
        && SameNullable(companion.FromStationId, infant.FromStationId)
        && SameNullable(companion.ToStationId, infant.ToStationId)
        && SameNullable(companion.FromStopOrder, infant.FromStopOrder)
        && SameNullable(companion.ToStopOrder, infant.ToStopOrder);

    private static bool SameNullable<T>(T? left, T? right) where T : struct =>
        !left.HasValue || !right.HasValue || EqualityComparer<T>.Default.Equals(left.Value, right.Value);

    private static int PassengerLegOrder(BookingPassenger passenger) =>
        passenger.TripId.HasValue ? 0 : 1;
}
