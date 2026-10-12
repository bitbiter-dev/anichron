namespace Anichron.API.Settings;

public sealed record JwtSettings
{
    // Stryker disable once String
    public string Secret { get; init; } = string.Empty;
    // Stryker disable once String
    public string Issuer { get; init; } = string.Empty;
    // Stryker disable once String
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = AppDefaults.Jwt.AccessTokenMinutes;
    public int RefreshTokenDays { get; init; } = AppDefaults.Jwt.RefreshTokenDays;
}
