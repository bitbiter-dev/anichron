using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Anichron.API.Infrastructure;

internal static class HealthCheckResponseWriter
{
    internal static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.Headers.CacheControl = "no-store";
        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            components = report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status.ToString().ToLowerInvariant())
        };
        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }
}
