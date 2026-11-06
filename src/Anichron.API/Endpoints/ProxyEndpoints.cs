using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

/// <summary>The proxy route, the one route that accepts the media cookie as well as the bearer header (ADR 0013).</summary>
public static class ProxyEndpoints
{
    internal const string MediaCookieName = "media_token";
    internal const string MediaCookiePath = $"{ApiPaths.Base}/{ApiPaths.Proxies.Group}";

    private const string mediaCookieScheme = "MediaCookie";
    private const string currentProxyCacheControl = "private, max-age=31536000, immutable";
    private const string staleProxyCacheControl = "private, no-cache";

    public static IServiceCollection AddProxyEndpoints(this IServiceCollection services)
    {
        services.AddAuthentication().AddJwtBearer(mediaCookieScheme, options => options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies[MediaCookieName];
                if (context.Token is null)
                    context.NoResult();
                return Task.CompletedTask;
            },
        });
        return services.AddJwtValidation(mediaCookieScheme);
    }

    public static IEndpointRouteBuilder MapProxyEndpoints(this IEndpointRouteBuilder app)
    {
        var proxies = app.MapGroup(ApiPaths.Proxies.Group).WithTags("Media").RequireUser(BearerOrMediaCookie());
        proxies.MapGet(ApiPaths.Proxies.Proxy, GetProxyAsync)
               .ProducesProxy(StatusCodes.Status200OK)
               .ProducesProxy(StatusCodes.Status206PartialContent)
               .Produces(StatusCodes.Status304NotModified)
               .Produces(StatusCodes.Status404NotFound)
               .Produces(StatusCodes.Status412PreconditionFailed)
               .ProducesWithoutBody(StatusCodes.Status416RangeNotSatisfiable);
        return app;
    }

    private static AuthorizationPolicy BearerOrMediaCookie()
        => new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme, mediaCookieScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new MustChangePasswordRequirement())
            .Build();

    private static RouteHandlerBuilder ProducesProxy(this RouteHandlerBuilder builder, int statusCode)
    {
        var contentTypes = ProxyKinds.All.Select(kind => kind.ContentType).Distinct().ToArray();
        return builder.Produces<Stream>(statusCode, contentTypes[0], contentTypes[1..]);
    }

    internal static async Task<IResult> GetProxyAsync(
        Guid assetId,
        string type,
        [FromQuery(Name = "v")] string? version,
        ClaimsPrincipal user,
        [FromServices] IMediaAssetReadRepository assets,
        [FromServices] IProxyStore proxies,
        HttpContext context,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        if (ProxyKinds.FromSlug(type) is not { } kind)
            return Results.NotFound();

        var proxy = await assets.FindVisibleProxyAsync(assetId, userId, kind.Type, ct);
        if (proxy is null)
            return Results.NotFound();

        if (proxies.OpenRead(proxy.ProxyPath) is not { } stream)
            return Results.NotFound();

        context.Response.Headers.CacheControl = version == proxy.ContentHash ? currentProxyCacheControl : staleProxyCacheControl;
        return Results.Stream(
            stream,
            kind.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{proxy.ContentHash}-{kind.Type}\""),
            enableRangeProcessing: true);
    }
}
