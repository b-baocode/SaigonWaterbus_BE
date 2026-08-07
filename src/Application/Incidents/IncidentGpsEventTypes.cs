namespace SaigonWaterbus.Application.Incidents;

public static class IncidentGpsEventTypes
{
    public const string RescueArrived = "RescueArrived";
    public const string ReplacementArrived = "ReplacementArrived";
    public const string PassengerTransferCompleted = "PassengerTransferCompleted";
    public const string TowingStarted = "TowingStarted";
    public const string TowingCompleted = "TowingCompleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        RescueArrived,
        ReplacementArrived,
        PassengerTransferCompleted,
        TowingStarted,
        TowingCompleted
    };
}
