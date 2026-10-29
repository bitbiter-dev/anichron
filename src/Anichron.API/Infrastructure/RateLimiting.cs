using Anichron.API.Endpoints;
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

    internal static Task RejectAsync(HttpContext httpContext, TimeSpan retryAfter)
    {
        httpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(InvariantCulture);
        return AuthProblems.Problem(
                StatusCodes.Status429TooManyRequests, AuthErrorCodes.RateLimited, AuthMessages.TooManyRequests)
            .ExecuteAsync(httpContext);
    }
}

/// <summary>
/// A per-client sliding window. A rejection asks the client to wait one whole window, because a
/// sliding window's lease reports no retry time and every permit is back once a window has passed.
/// </summary>
/// <param name="permitLimit">The requests one client may make per window.</param>
/// <param name="window">The window.</param>
/// <param name="segmentsPerWindow">The segments the window slides by.</param>
internal sealed class SlidingWindowPolicy(int permitLimit, TimeSpan window, int segmentsPerWindow) : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected
        => (context, _) => new ValueTask(RateLimiting.RejectAsync(context.HttpContext, window));

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
        => RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: RateLimiting.PartitionKey(httpContext),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                SegmentsPerWindow = segmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
}
