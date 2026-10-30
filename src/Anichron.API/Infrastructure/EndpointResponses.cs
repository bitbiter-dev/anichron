using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Anichron.API.Infrastructure;

/// <summary>
/// Requirements that declare the responses they cause, and the rule that describes every error
/// without a body as the Problem Details the status code pages write for it.
/// </summary>
internal static class EndpointResponses
{
    internal const string ProblemContentType = "application/problem+json";

    extension<TBuilder>(TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>A signed-in user, refused while their password must change.</summary>
        internal TBuilder RequireUser()
            => builder.RequireUserAllowedWhileMustChangePassword().ProducesProblem(StatusCodes.Status403Forbidden);

        internal TBuilder RequireUserAllowedWhileMustChangePassword()
            => builder.RequireAuthorization().ProducesProblem(StatusCodes.Status401Unauthorized);

        internal TBuilder RequireAdmin()
            => builder.RequireAuthorization(AuthPolicies.Admin)
                      .ProducesProblem(StatusCodes.Status401Unauthorized)
                      .ProducesProblem(StatusCodes.Status403Forbidden);

        internal TBuilder RequireRateLimit(string policy)
            => builder.RequireRateLimiting(policy).ProducesProblem(StatusCodes.Status429TooManyRequests);

        /// <summary>An error the framework answers with an empty body that the status code pages leave alone.</summary>
        /// <param name="statusCode">The response status.</param>
        internal TBuilder ProducesWithoutBody(int statusCode)
            => builder.WithMetadata(
                new ProducesResponseTypeMetadata(statusCode, typeof(void)), new ResponseWithoutBody(statusCode));
    }

    /// <summary>
    /// Adds the 400 and 415 a request body can fail to bind with, then describes every error
    /// response still declared without a body as Problem Details.
    /// </summary>
    /// <param name="endpoint">The endpoint, after all its other conventions.</param>
    internal static void DescribeErrorsAsProblems(EndpointBuilder endpoint)
    {
        var metadata = endpoint.Metadata;
        if (metadata.OfType<IAcceptsMetadata>().Any())
        {
            var undeclared = new[] { StatusCodes.Status400BadRequest, StatusCodes.Status415UnsupportedMediaType }
                .Where(status => !metadata.OfType<IProducesResponseTypeMetadata>().Any(declared => declared.StatusCode == status))
                .ToList();
            foreach (var status in undeclared)
                metadata.Add(new ProducesResponseTypeMetadata(status));
        }

        var bodiless = metadata.OfType<IProducesResponseTypeMetadata>()
            .GroupBy(declared => declared.StatusCode)
            .Select(declarations => declarations.Last())
            .Where(published => published.StatusCode >= StatusCodes.Status400BadRequest
                                && (published.Type is null || published.Type == typeof(void))
                                && !metadata.OfType<ResponseWithoutBody>().Any(kept => kept.StatusCode == published.StatusCode))
            .Select(published => published.StatusCode)
            .ToList();
        foreach (var status in bodiless)
            metadata.Add(new ProducesResponseTypeMetadata(status, typeof(ProblemDetails), [ProblemContentType]));
    }
}

internal sealed record ResponseWithoutBody(int StatusCode);
