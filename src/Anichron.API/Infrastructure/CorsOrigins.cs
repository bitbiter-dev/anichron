using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Anichron.API.Infrastructure;

internal static class CorsOrigins
{
    internal static string[] Read(IConfiguration configuration)
        => [.. ConfigurationList.Parse(configuration["Cors:AllowedOrigins"], static origin => origin)];

    // With no configured origins the default policy allows nothing (same-origin behavior).
    internal static void ConfigureDefaultPolicy(CorsOptions options, string[] allowedOrigins)
    {
        if (allowedOrigins.Length > 0)
        {
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        }
    }

    // SameSite=None is required when the UI and API are on different origins so browsers
    // send the cookie on cross-origin requests. SameSite=Strict is safer for same-origin.
    internal static SameSiteMode RefreshCookieSameSite(string[] allowedOrigins)
        => allowedOrigins.Length > 0 ? SameSiteMode.None : SameSiteMode.Strict;
}
