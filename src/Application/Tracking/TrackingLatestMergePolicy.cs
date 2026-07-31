namespace SaigonWaterbus.Application.Tracking;

public static class TrackingLatestMergePolicy
{
    public const string LiveAuthorityReplacement = "live-authority";
    public const string OlderRejectedReason = "older";
    public const string LiveStaleRejectedReason = "live-stale";

    public static TrackingLatestMergeDecision Decide(
        DateTimeOffset? lastRecordedAt,
        long? lastSequence,
        DateTimeOffset recordedAt,
        long sequence,
        string? source,
        DateTimeOffset now,
        TimeSpan liveAuthorityWindow,
        string? lastSource = null,
        DateTimeOffset? lastLiveAuthorityUntil = null,
        bool incomingBreaksLiveAuthority = false)
    {
        if (!lastRecordedAt.HasValue)
        {
            return TrackingLatestMergeDecision.Updated();
        }

        if (IsActiveLiveAuthority(lastSource, lastLiveAuthorityUntil, now)
            && !IsLiveSource(source)
            && !incomingBreaksLiveAuthority)
        {
            return TrackingLatestMergeDecision.Ignored(OlderRejectedReason);
        }

        if (recordedAt > lastRecordedAt.Value)
        {
            return TrackingLatestMergeDecision.Updated();
        }

        if (recordedAt == lastRecordedAt.Value && sequence > lastSequence.GetValueOrDefault())
        {
            return TrackingLatestMergeDecision.Updated();
        }

        if (!IsLiveSource(source))
        {
            return TrackingLatestMergeDecision.Ignored(OlderRejectedReason);
        }

        return IsWithinLiveAuthorityWindow(recordedAt, now, liveAuthorityWindow)
            ? TrackingLatestMergeDecision.Updated(LiveAuthorityReplacement)
            : TrackingLatestMergeDecision.Ignored(LiveStaleRejectedReason);
    }

    public static bool CanLiveAuthorityOverride(
        string? source,
        DateTimeOffset recordedAt,
        DateTimeOffset now,
        TimeSpan liveAuthorityWindow) =>
        IsLiveSource(source) && IsWithinLiveAuthorityWindow(recordedAt, now, liveAuthorityWindow);

    public static bool IsLiveSource(string? source) =>
        string.Equals(source, "live", StringComparison.OrdinalIgnoreCase);

    public static bool IsActiveLiveAuthority(
        string? source,
        DateTimeOffset? liveAuthorityUntil,
        DateTimeOffset now) =>
        IsLiveSource(source)
        && liveAuthorityUntil.HasValue
        && liveAuthorityUntil.Value >= now;

    private static bool IsWithinLiveAuthorityWindow(
        DateTimeOffset recordedAt,
        DateTimeOffset now,
        TimeSpan liveAuthorityWindow) =>
        recordedAt >= now.Subtract(liveAuthorityWindow);
}

public sealed record TrackingLatestMergeDecision(
    bool LatestUpdated,
    string? ReplacedBy,
    string? Rejected)
{
    public static TrackingLatestMergeDecision Updated(string? replacedBy = null) =>
        new(true, replacedBy, null);

    public static TrackingLatestMergeDecision Ignored(string rejected) =>
        new(false, null, rejected);
}
