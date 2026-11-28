using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public sealed record PatchInteractionRequest(bool? Starred = null, bool? Liked = null, bool? Hidden = null);

public sealed record PurgeRequest([property: Length(1, MediaEndpoints.MaxPurgedAssets)] IReadOnlyList<Guid> AssetIds);

/// <summary>What a purge request queued.</summary>
/// <param name="Accepted">The requested ids that will be purged, plus the videos of accepted Live Photos.</param>
public sealed record PurgeAcceptedDto(IReadOnlyList<Guid> Accepted);

public static class MediaEndpoints
{
    internal const int PageSize = 60;
    internal const string InvalidCursorMessage = "after must be the next cursor of an earlier page.";
    internal const int MaxPurgedAssets = 1000;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var assets = app.MapGroup(ApiPaths.Assets.Group).WithTags("Media").RequireUser();
        assets.MapPatch(ApiPaths.Assets.Interaction, PatchInteractionAsync)
              .Produces<AssetInteractionDto>()
              .Produces(StatusCodes.Status404NotFound);
        assets.MapGet(ApiPaths.Assets.Hidden, GetHiddenAsync)
              .Produces<HiddenAssetsPageDto>()
              .ProducesValidationProblem();
        assets.MapGet(ApiPaths.Assets.SoftDeleted, GetSoftDeletedAsync)
              .Produces<SoftDeletedAssetsPageDto>()
              .ProducesValidationProblem();
        assets.MapPost(ApiPaths.Assets.Purges, RequestPurgeAsync);
        return app;
    }

    /// <summary>Queues a purge of the caller's soft-deleted assets, one work item per storage config.</summary>
    /// <param name="request">The assets to purge.</param>
    /// <param name="user">The caller.</param>
    /// <param name="reader">Decides which ids are accepted.</param>
    /// <param name="queue">Takes the purges.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>202 with the accepted ids; ids outside the caller's soft-deleted assets are left out without a trace.</returns>
    internal static async Task<Results<Accepted<PurgeAcceptedDto>, ValidationProblem, UnauthorizedHttpResult>> RequestPurgeAsync(
        PurgeRequest request,
        ClaimsPrincipal user,
        [FromServices] ISoftDeletedAssetReader reader,
        [FromServices] IWorkQueue queue,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return TypedResults.Unauthorized();
        if (request.AssetIds is { Count: 0 or > MaxPurgedAssets })
            return ApiProblems.InvalidField(ErrorCodes.InvalidAssetCount, "assetIds", $"assetIds must hold from 1 to {MaxPurgedAssets} ids.");

        var requested = request.AssetIds.Distinct().ToList();

        var accepted = await reader.ResolvePurgeAsync(userId, requested, ct);
        foreach (var config in accepted.GroupBy(target => target.StorageConfigId))
            await queue.EnqueueAsync(new AssetPurgePayload(config.Key, [.. config.Select(target => target.AssetId)]), userId, ct);

        return TypedResults.Accepted((string?)null, new PurgeAcceptedDto([.. accepted.Select(target => target.AssetId)]));
    }

    internal static async Task<IResult> GetSoftDeletedAsync(
        string? after,
        ClaimsPrincipal user,
        [FromServices] ISoftDeletedAssetReader reader,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        AssetCursor? cursor = null;
        if (after is not null && (cursor = AssetCursors.Parse(after)) is null)
            return InvalidCursor();

        var page = await reader.ReadPageAsync(userId, cursor, PageSize, ct);
        return Results.Ok(new SoftDeletedAssetsPageDto(
            [.. page.Items.Select(AssetDtoMapper.ToDto)],
            page.Next is { } next ? AssetCursors.Format(next) : null,
            page.Total));
    }

    private static IResult InvalidCursor()
        => ApiProblems.FieldProblem(StatusCodes.Status400BadRequest, ErrorCodes.InvalidCursor, "after", InvalidCursorMessage);

    internal static async Task<IResult> GetHiddenAsync(
        string? after,
        ClaimsPrincipal user,
        [FromServices] IAssetViewReader reader,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        AssetCursor? cursor = null;
        if (after is not null && (cursor = AssetCursors.Parse(after)) is null)
            return InvalidCursor();

        var views = await reader.ReadAsync(userId, new HiddenAssets(cursor, PageSize + 1), ct);
        var page = views.Take(PageSize).ToList();
        return Results.Ok(new HiddenAssetsPageDto(
            [.. page.Select(view => new HiddenAssetDto(AssetDtoMapper.ToDto(view, burst: null), AssetCursors.FormatInstant(view.HiddenAt!.Value)))],
            views.Count > PageSize ? AssetCursors.Format(new AssetCursor(page[^1].HiddenAt!.Value, page[^1].Id)) : null));
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
