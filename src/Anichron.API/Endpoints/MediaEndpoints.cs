using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public static class MediaEndpoints
{
    internal const string ProxyCacheControl = "private, max-age=31536000, immutable";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Assets.Group).WithTags("Media");
        group.MapGet(ApiPaths.Assets.Proxy, GetProxyAsync)
             .RequireAuthorization();
        return app;
    }

    internal static (ProxyType Type, string ContentType)? ParseProxyKind(string type)
        => type switch
        {
            ApiPaths.Assets.ThumbnailProxy => (ProxyType.Thumbnail, "image/jpeg"),
            ApiPaths.Assets.PreviewProxy => (ProxyType.FullPreview, "image/jpeg"),
            ApiPaths.Assets.VideoProxy => (ProxyType.WebVideo, "video/mp4"),
            _ => null,
        };

    internal static async Task<IResult> GetProxyAsync(
        Guid assetId,
        string type,
        ClaimsPrincipal user,
        [FromServices] IMediaAssetReadRepository assets,
        [FromServices] IProxyStore proxies,
        HttpContext context,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        if (ParseProxyKind(type) is not { } kind)
            return Results.NotFound();

        var proxy = await assets.FindVisibleProxyAsync(assetId, userId, kind.Type, ct);
        if (proxy is null)
            return Results.NotFound();

        if (proxies.OpenRead(proxy.ProxyPath) is not { } stream)
            return Results.NotFound();

        context.Response.Headers.CacheControl = ProxyCacheControl;
        return Results.Stream(
            stream,
            kind.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{proxy.ContentHash}-{kind.Type}\""),
            enableRangeProcessing: true);
    }
}
