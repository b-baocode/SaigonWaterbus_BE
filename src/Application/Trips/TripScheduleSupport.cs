namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Kiem tra lich chay cua tau: mot tau khong duoc gan cho 2 chuyen co thoi gian chong lan,
/// va giua 2 chuyen lien tiep phai co thoi gian quay dau toi thieu (BoatTurnaroundBuffer).
/// </summary>
internal static class TripScheduleSupport
{
    /// <summary>Thoi gian quay dau toi thieu giua 2 chuyen cua cung mot tau.</summary>
    public static readonly TimeSpan BoatTurnaroundBuffer = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Hai chuyen co dung chung tau bi coi la xung dot khi khoang thoi gian cua chung
    /// giao nhau, HOAC khoang trong giua chung nho hon thoi gian quay dau.
    /// </summary>
    public static bool ConflictsWithBuffer(
        DateTimeOffset existingDeparture,
        DateTimeOffset existingArrival,
        DateTimeOffset newDeparture,
        DateTimeOffset newArrival) =>
        existingDeparture < newArrival.Add(BoatTurnaroundBuffer)
        && newDeparture.Subtract(BoatTurnaroundBuffer) < existingArrival;

    public static string BuildConflictMessage(
        string tripCode,
        DateTimeOffset departure,
        DateTimeOffset arrival)
    {
        var localDeparture = departure.ToOffset(TimeSpan.FromHours(7));
        var localArrival = arrival.ToOffset(TimeSpan.FromHours(7));

        return $"Tàu đã có chuyến {tripCode} chạy từ {localDeparture:HH:mm dd/MM/yyyy} đến {localArrival:HH:mm dd/MM/yyyy}. "
            + $"Hai chuyến của cùng một tàu không được chồng giờ và phải cách nhau tối thiểu "
            + $"{BoatTurnaroundBuffer.TotalMinutes:0} phút để quay đầu.";
    }
}
