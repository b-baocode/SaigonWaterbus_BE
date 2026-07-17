namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IIncidentGpsHookNotifier
{
    Task NotifyAsync(
        IncidentGpsHookNotification notification,
        CancellationToken cancellationToken);
}

public sealed record IncidentGpsHookNotification(
    string Event,
    Guid IncidentId,
    string BoatCode,
    string? ReplacementBoatCode,
    decimal? Lat,
    decimal? Lng);

public sealed class NullIncidentGpsHookNotifier : IIncidentGpsHookNotifier
{
    public static readonly NullIncidentGpsHookNotifier Instance = new();

    private NullIncidentGpsHookNotifier() { }

    public Task NotifyAsync(
        IncidentGpsHookNotification notification,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
