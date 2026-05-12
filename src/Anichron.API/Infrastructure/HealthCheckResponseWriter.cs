using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anichron.API.Infrastructure;

internal static class HealthCheckResponseWriter
{
    internal static async Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            components = report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status.ToString().ToLowerInvariant())
        };
        await context.Response.WriteAsJsonAsync(response);
    }
}
