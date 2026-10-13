using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Infrastructure;

internal static class RateLimiting
{
    // RemoteIpAddress is populated by UseForwardedHeaders before this middleware runs.
    // Requests with no resolvable IP share a tight "unresolved" bucket to prevent IP-hiding abuse.
    internal static string PartitionKey(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unresolved";

    internal static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken token)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = AuthMessages.TooManyRequests }, token);
    }
}
