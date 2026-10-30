using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public static class BurstEndpoints
{
    public static IEndpointRouteBuilder MapBurstEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Bursts.Group).WithTags("Bursts").RequireUser();
        group.MapGet(ApiPaths.Bursts.Members, GetMembersAsync)
             .Produces<IReadOnlyList<AssetDto>>()
             .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    internal static async Task<IResult> GetMembersAsync(
        Guid burstId,
        ClaimsPrincipal user,
        [FromServices] IAssetViewReader reader,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        var members = await reader.ReadAsync(userId, new BurstMembers(burstId), ct);
        return members.Count == 0
            ? Results.NotFound()
            : Results.Ok(Bursts.Members(burstId, members));
    }
}
