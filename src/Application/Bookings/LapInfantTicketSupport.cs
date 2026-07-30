using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Bookings;

internal static class LapInfantTicketSupport
{
    private const string InfantTicketTypeCode = "INFANT";
    private const string AdultTicketTypeCode = "ADULT";
    /// <summary>Prefix lưu tên ADULT đi kèm từ form vào BookingPassenger.Note.</summary>
    internal const string CompanionNotePrefix = "COMPANION:";

    public static bool IsLapInfant(BookingPassenger passenger) =>
        string.Equals(passenger.PassengerType?.Trim(), InfantTicketTypeCode, StringComparison.OrdinalIgnoreCase)
        && passenger.TripId.HasValue
        && !passenger.TripSeatId.HasValue
        && passenger.TripSeat is null;

    public static bool UsesCompanionTicket(BookingPassenger passenger) =>
        IsLapInfant(passenger);

    public static bool RequiresOwnTicket(BookingPassenger passenger) =>
        !UsesCompanionTicket(passenger);

    /// <summary>
    /// Lưu companionPassengerName từ FE vào Note (INFANT), giữ Note người dùng nếu có.
    /// </summary>
    public static string? BuildPassengerNote(BookingItemRequest item)
    {
        var userNote = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim();
        var companionName = string.IsNullOrWhiteSpace(item.CompanionPassengerName)
            ? null
            : item.CompanionPassengerName.Trim();
        var isInfant = string.Equals(
            item.TicketTypeCode?.Trim(),
            InfantTicketTypeCode,
            StringComparison.OrdinalIgnoreCase);

        if (isInfant && !string.IsNullOrWhiteSpace(companionName))
        {
            var marker = CompanionNotePrefix + companionName;
            return string.IsNullOrWhiteSpace(userNote) ? marker : $"{marker}\n{userNote}";
        }

        return userNote;
    }

    public static string? GetRequestedCompanionName(BookingPassenger passenger)
    {
        var note = passenger.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var firstLine = note.Split('\n', 2, StringSplitOptions.None)[0].Trim();
        if (!firstLine.StartsWith(CompanionNotePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = firstLine[CompanionNotePrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static IReadOnlyDictionary<Guid, Guid> AssignInfantsToCompanions(
        IEnumerable<BookingPassenger> passengers) =>
        AssignCompanionTicketPassengersToAdults(passengers);

    public static IReadOnlyDictionary<Guid, Guid> AssignCompanionTicketPassengersToAdults(
        IEnumerable<BookingPassenger> passengers)
    {
        var list = passengers.ToList();
        var companions = list
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

        var dependents = list
            .Where(UsesCompanionTicket)
            .OrderBy(PassengerLegOrder)
            .ThenBy(x => x.FromStopOrder ?? int.MaxValue)
            .ThenBy(x => x.ToStopOrder ?? int.MaxValue)
            .ThenBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToList();

        // Pass 1: tôn trọng companionPassengerName FE đã lưu (cùng booking + chặng).
        foreach (var dependent in dependents)
        {
            var requestedName = NormalizeName(GetRequestedCompanionName(dependent));
            if (string.IsNullOrEmpty(requestedName))
            {
                continue;
            }

            var companion = companions.FirstOrDefault(candidate =>
                !usedCompanionIds.Contains(candidate.Id)
                && candidate.BookingId == dependent.BookingId
                && SameTicketSegment(candidate, dependent)
                && NormalizeName(candidate.FullName) == requestedName);
            if (companion is null)
            {
                continue;
            }

            assignmentByPassengerId[dependent.Id] = companion.Id;
            usedCompanionIds.Add(companion.Id);
        }

        // Pass 2: infant còn lại — ADULT cùng booking/chặng (theo ghế), rồi mới booking khác.
        foreach (var dependent in dependents)
        {
            if (assignmentByPassengerId.ContainsKey(dependent.Id))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(NormalizeName(GetRequestedCompanionName(dependent))))
            {
                continue;
            }

            var companion = companions.FirstOrDefault(candidate =>
                    !usedCompanionIds.Contains(candidate.Id)
                    && SameTicketSegment(candidate, dependent)
                    && candidate.BookingId == dependent.BookingId)
                ?? companions.FirstOrDefault(candidate =>
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

    private static string NormalizeName(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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
