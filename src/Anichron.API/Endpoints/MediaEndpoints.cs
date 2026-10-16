using Anichron.API.Infrastructure;
using Anichron.API.Settings;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.IO.Abstractions;
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
            "thumbnail" => (ProxyType.Thumbnail, "image/jpeg"),
            "preview" => (ProxyType.FullPreview, "image/jpeg"),
            "video" => (ProxyType.WebVideo, "video/mp4"),
            _ => null,
        };

    internal static async Task<IResult> GetProxyAsync(
        Guid assetId,
        string type,
        ClaimsPrincipal user,
        [FromServices] IMediaAssetReadRepository assets,
        [FromServices] IFileSystem fileSystem,
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

        const string root = AppDefaults.Storage.ProxyPath;
        var fullPath = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, proxy.ProxyPath));
        if (!fullPath.StartsWith(root + "/", StringComparison.Ordinal))
            return Results.NotFound();

        Stream stream;
        try
        {
            stream = fileSystem.FileStream.New(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = ProxyCacheControl;
        return Results.Stream(
            stream,
            kind.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{proxy.ContentHash}-{type}\""),
            enableRangeProcessing: true);
    }
}
