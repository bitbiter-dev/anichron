namespace Anichron.API.Settings;

public sealed record CorsSettings
{
    public string[] AllowedOrigins { get; init; } = [];
}
