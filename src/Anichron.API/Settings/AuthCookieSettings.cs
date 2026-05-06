namespace Anichron.API.Settings;

public sealed record AuthCookieSettings
{
    public SameSiteMode SameSite { get; init; } = SameSiteMode.Strict;
    public int RefreshTokenDays { get; init; } = AppDefaults.Jwt.RefreshTokenDays;
}
