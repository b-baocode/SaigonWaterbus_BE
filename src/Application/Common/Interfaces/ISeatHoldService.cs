namespace SaigonWaterbus.Application.Common.Interfaces;

/// <summary>
/// Một lượt giữ ghế tạm trên chặng [FromStopOrder, ToStopOrder) theo stop_order của tuyến.
/// Chặng "cả trip" (sightseeing / không chọn chặng) dùng (int.MinValue, int.MaxValue).
/// </summary>
public sealed record SeatHoldInfo(Guid UserId, int FromStopOrder, int ToStopOrder);

public interface ISeatHoldService
{
    /// <summary>
    /// Giữ tạm các ghế cho user trên chặng [fromStopOrder, toStopOrder) trong thời hạn ttl.
    /// Trả về danh sách tripSeatId KHÔNG giữ được (user khác đang giữ chặng giao nhau).
    /// Ghế user này đã giữ trước đó trên cùng chặng sẽ được gia hạn TTL.
    /// </summary>
    Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        int fromStopOrder,
        int toStopOrder,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>Nhả mọi lượt giữ của chính user này trên các ghế. Lượt giữ của người khác bỏ qua.</summary>
    Task ReleaseAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Map tripSeatId → các lượt giữ còn hiệu lực của một chuyến.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken);

    /// <summary>Xóa toàn bộ lượt giữ ghế của một trip khi Admin reset dữ liệu demo.</summary>
    Task ClearTripAsync(Guid tripId, CancellationToken cancellationToken);
}

public sealed class NullSeatHoldService : ISeatHoldService
{
    public static readonly NullSeatHoldService Instance = new();

    private NullSeatHoldService() { }

    public Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        int fromStopOrder,
        int toStopOrder,
        TimeSpan ttl,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task ReleaseAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<SeatHoldInfo>>>(
            new Dictionary<Guid, IReadOnlyList<SeatHoldInfo>>());

    public Task ClearTripAsync(Guid tripId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
