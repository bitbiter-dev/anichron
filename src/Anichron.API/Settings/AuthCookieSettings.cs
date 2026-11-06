namespace Anichron.API.Settings;

public sealed record AuthCookieSettings
{
    public SameSiteMode SameSite { get; init; } = SameSiteMode.Strict;
    public int AccessTokenMinutes { get; init; } = AppDefaults.Jwt.AccessTokenMinutes;
    public int RefreshTokenDays { get; init; } = AppDefaults.Jwt.RefreshTokenDays;
}
