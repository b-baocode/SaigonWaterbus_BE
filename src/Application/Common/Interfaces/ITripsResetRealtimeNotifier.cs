namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ITripsResetRealtimeNotifier
{
    Task PublishResetAsync(
        TripsResetRealtimeEvent resetEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Bắn cho GPS/boat group khi admin reset trip demo:
///   - RemovedTrips: các trip bị xoá (GPS phải return-to-base tàu về đúng bến cuối của trip).
///   - AddedTrips: trip mới vừa tạo (GPS biết bến khởi hành để hiển thị chuyến chờ).
///   - KeptActiveTrips: trip đang chạy thực tế, được giữ lại (GPS tiếp tục dùng).
/// </summary>
public sealed record TripsResetRealtimeEvent(
    Guid BoatId,
    string BoatCode,
    DateOnly OperatingDate,
    IReadOnlyList<TripResetRemovedRealtimeEvent> RemovedTrips,
    IReadOnlyList<TripResetAddedRealtimeEvent> AddedTrips,
    IReadOnlyList<TripResetKeptRealtimeEvent> KeptActiveTrips);

public sealed record TripResetRemovedRealtimeEvent(
    Guid TripId,
    string TripCode,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    string? EndStationCode,
    string? EndStationName);

public sealed record TripResetAddedRealtimeEvent(
    Guid TripId,
    string TripCode,
    string RouteCode,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    string? StartStationCode,
    string? StartStationName,
    string? EndStationCode,
    string? EndStationName);

public sealed record TripResetKeptRealtimeEvent(
    Guid TripId,
    string TripCode,
    string Reason);

public sealed class NullTripsResetRealtimeNotifier : ITripsResetRealtimeNotifier
{
    public static readonly NullTripsResetRealtimeNotifier Instance = new();

    private NullTripsResetRealtimeNotifier() { }

    public Task PublishResetAsync(
        TripsResetRealtimeEvent resetEvent,
        CancellationToken cancellationToken) => Task.CompletedTask;
}