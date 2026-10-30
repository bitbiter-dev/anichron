using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public sealed record PatchInteractionRequest(bool? Starred, bool? Liked, bool? Hidden);

public static class MediaEndpoints
{
    internal const string ProxyCacheControl = "private, max-age=31536000, immutable";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Assets.Group).WithTags("Media").RequireUser();
        group.MapGet(ApiPaths.Assets.Proxy, GetProxyAsync)
             .ProducesProxy(StatusCodes.Status200OK)
             .ProducesProxy(StatusCodes.Status206PartialContent)
             .Produces(StatusCodes.Status304NotModified)
             .Produces(StatusCodes.Status404NotFound)
             .Produces(StatusCodes.Status412PreconditionFailed)
             .ProducesWithoutBody(StatusCodes.Status416RangeNotSatisfiable);
        group.MapPatch(ApiPaths.Assets.Interaction, PatchInteractionAsync)
             .Produces<AssetInteractionDto>()
             .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static RouteHandlerBuilder ProducesProxy(this RouteHandlerBuilder builder, int statusCode)
    {
        var contentTypes = ProxyKinds.All.Select(kind => kind.ContentType).Distinct().ToArray();
        return builder.Produces<Stream>(statusCode, contentTypes[0], contentTypes[1..]);
    }

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

        if (ProxyKinds.FromSlug(type) is not { } kind)
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

    internal static async Task<IResult> PatchInteractionAsync(
        Guid assetId,
        PatchInteractionRequest? request,
        ClaimsPrincipal user,
        [FromServices] IAssetInteractionWriter interactions,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var change = new InteractionChange(request?.Starred, request?.Liked, request?.Hidden);
        return await interactions.ApplyAsync(userId, assetId, change, ct) is { } state
            ? Results.Ok(AssetDtoMapper.ToDto(state))
            : Results.NotFound();
    }
}
