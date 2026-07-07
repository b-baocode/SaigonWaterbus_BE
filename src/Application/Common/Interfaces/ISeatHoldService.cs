namespace SaigonWaterbus.Application.Common.Interfaces;

public interface ISeatHoldService
{
    /// <summary>
    /// Giữ tạm các ghế cho user trong thời hạn ttl. Trả về danh sách tripSeatId KHÔNG giữ được
    /// (đang bị user khác giữ). Ghế user này đã giữ trước đó sẽ được gia hạn TTL.
    /// </summary>
    Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>Nhả các ghế do chính user này giữ. Ghế do người khác giữ sẽ bỏ qua.</summary>
    Task ReleaseAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Trả về map tripSeatId → userId đang giữ của một chuyến.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken);
}

public sealed class NullSeatHoldService : ISeatHoldService
{
    public static readonly NullSeatHoldService Instance = new();

    private NullSeatHoldService() { }

    public Task<IReadOnlyList<Guid>> TryHoldAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        TimeSpan ttl,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task ReleaseAsync(
        Guid tripId,
        IReadOnlyList<Guid> tripSeatIds,
        Guid userId,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<Guid, Guid>> GetHeldSeatsAsync(
        Guid tripId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());
}
