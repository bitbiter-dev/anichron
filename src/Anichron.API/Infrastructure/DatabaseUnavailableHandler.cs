using Anichron.API.Endpoints;
using Anichron.API.Settings;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using static System.Globalization.CultureInfo;

namespace Anichron.API.Infrastructure;

/// <summary>
/// Answers a request that failed because the database cannot be reached with a 503 the client can
/// retry, and leaves every other exception to the server error.
/// </summary>
/// <param name="logger">Notes each request answered with the 503.</param>
internal sealed partial class DatabaseUnavailableHandler(ILogger<DatabaseUnavailableHandler> logger) : IExceptionHandler
{
    internal const string Detail = "The service is temporarily unavailable. Try again shortly.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (Unreachable(exception) is not { } cause)
            return false;

        Log.DatabaseUnreachable(logger, httpContext.Request.Method, httpContext.Request.Path, cause.Message);
        httpContext.Response.Headers.RetryAfter = AppDefaults.Database.RetryAfterSeconds.ToString(InvariantCulture);
        await ApiProblems.Problem(StatusCodes.Status503ServiceUnavailable, ErrorCodes.ServiceUnavailable, Detail)
            .ExecuteAsync(httpContext);
        return true;
    }

    private static NpgsqlException? Unreachable(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is NpgsqlException { IsTransient: true } transient)
                return transient;
        }

        return null;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "{Method} {Path} answered 503: the database is unreachable ({Reason}).")]
        public static partial void DatabaseUnreachable(ILogger logger, string method, string path, string reason);
    }
}
