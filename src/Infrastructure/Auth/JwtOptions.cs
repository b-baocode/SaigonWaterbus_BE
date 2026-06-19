namespace SaigonWaterbus.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SaigonWaterbus";

    public string Audience { get; set; } = "SaigonWaterbus.Client";

    public string SigningKey { get; set; } = "ChangeThisJwtSigningKeyForProductionAtLeast32Chars!";

    public int AccessTokenMinutes { get; set; } = 300;

    public int RefreshTokenDays { get; set; } = 30;
}
