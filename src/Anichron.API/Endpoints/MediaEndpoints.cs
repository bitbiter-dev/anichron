using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public sealed record PatchInteractionRequest(bool? Starred, bool? Liked, bool? Hidden);

public static class MediaEndpoints
{
    internal const int HiddenPageSize = 60;
    internal const string InvalidCursorMessage = "after must be the next cursor of an earlier page.";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var assets = app.MapGroup(ApiPaths.Assets.Group).WithTags("Media").RequireUser();
        assets.MapPatch(ApiPaths.Assets.Interaction, PatchInteractionAsync)
              .Produces<AssetInteractionDto>()
              .Produces(StatusCodes.Status404NotFound);
        assets.MapGet(ApiPaths.Assets.Hidden, GetHiddenAsync)
              .Produces<HiddenAssetsPageDto>()
              .ProducesValidationProblem();
        return app;
    }

    internal static async Task<IResult> GetHiddenAsync(
        string? after,
        ClaimsPrincipal user,
        [FromServices] IAssetViewReader reader,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        HiddenCursor? cursor = null;
        if (after is not null && (cursor = HiddenCursors.Parse(after)) is null)
            return ApiProblems.FieldProblem(StatusCodes.Status400BadRequest, ErrorCodes.InvalidCursor, nameof(after), InvalidCursorMessage);

        var views = await reader.ReadAsync(userId, new HiddenAssets(cursor, HiddenPageSize + 1), ct);
        var page = views.Take(HiddenPageSize).ToList();
        return Results.Ok(new HiddenAssetsPageDto(
            [.. page.Select(view => new HiddenAssetDto(AssetDtoMapper.ToDto(view, burst: null), HiddenCursors.FormatHiddenAt(view.HiddenAt!.Value)))],
            views.Count > HiddenPageSize ? HiddenCursors.Format(page[^1]) : null));
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
