using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Mvc;
using NodaTime.Text;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public sealed record OnThisDayDto(string Date, IReadOnlyList<FlashbackYearDto> Years);

public static class FlashbackEndpoints
{
    internal const string InvalidDateMessage = "date must be a calendar date in the form YYYY-MM-DD.";

    public static IEndpointRouteBuilder MapFlashbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Flashbacks.Group).WithTags("Flashbacks");
        group.MapGet(ApiPaths.Flashbacks.OnThisDay, GetOnThisDayAsync)
             .RequireAuthorization();
        return app;
    }

    internal static async Task<IResult> GetOnThisDayAsync(
        string? date,
        ClaimsPrincipal user,
        [FromServices] IAssetViewReader reader,
        [FromServices] IClock clock,
        [FromServices] DateTimeZone zone,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();

        LocalDate day;
        if (date is null)
            day = clock.GetCurrentInstant().InZone(zone).Date;
        else if (LocalDatePattern.Iso.Parse(date) is { Success: true } parsed)
            day = parsed.Value;
        else
            return ApiProblems.FieldProblem(StatusCodes.Status400BadRequest, ErrorCodes.InvalidDate, nameof(date), InvalidDateMessage);

        var selection = new OnThisDay(day);
        var views = await reader.ReadAsync(userId, selection, ct);
        return Results.Ok(new OnThisDayDto(LocalDatePattern.Iso.Format(day), FlashbackYears.Group(views, selection)));
    }
}
