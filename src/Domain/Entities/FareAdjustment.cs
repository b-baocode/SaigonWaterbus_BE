using SaigonWaterbus.Domain.Common;

namespace SaigonWaterbus.Domain.Entities;

public static class FareAdjustmentScopes
{
    public const string Weekend = "Weekend";
    public const string Holiday = "Holiday";
    public const string Special = "Special";

    public static bool IsValid(string? scope) =>
        string.Equals(scope, Weekend, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scope, Holiday, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scope, Special, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string scope) =>
        scope.Trim().ToUpperInvariant() switch
        {
            "WEEKEND" => Weekend,
            "HOLIDAY" => Holiday,
            "SPECIAL" => Special,
            _ => scope.Trim()
        };
}

public class FareAdjustment : BaseGuidAuditableEntity
{
    public string Scope { get; set; } = FareAdjustmentScopes.Holiday;
    public DateOnly? Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal SurchargePercent { get; set; }
    public decimal RoundingStep { get; set; } = 1000m;
    public bool IsActive { get; set; } = true;
}
