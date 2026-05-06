namespace Anichron.API.Settings;

public sealed record JwtSettings
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = AppDefaults.Jwt.AccessTokenMinutes;
    public int RefreshTokenDays { get; init; } = AppDefaults.Jwt.RefreshTokenDays;
}
