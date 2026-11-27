using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Microsoft.AspNetCore.Mvc;
using NodaTime.Text;
using System.Globalization;
using System.Security.Claims;

namespace Anichron.API.Endpoints;

public sealed record FlashbackYearDto(int Year, IReadOnlyList<AssetDto> Assets);

public sealed record OnThisDayDto(string Date, IReadOnlyList<FlashbackYearDto> Years);

public sealed record RecentDayDto(string Date, int Year, AssetDto Memory);

public sealed record RecentDaysDto(IReadOnlyList<RecentDayDto> Days);

public static class FlashbackEndpoints
{

    public static IEndpointRouteBuilder MapFlashbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(ApiPaths.Flashbacks.Group).WithTags("Flashbacks").RequireUser();
        group.MapGet(ApiPaths.Flashbacks.OnThisDay, GetOnThisDayAsync)
             .Produces<OnThisDayDto>()
             .ProducesValidationProblem();
        group.MapGet(ApiPaths.Flashbacks.Days, GetRecentDaysAsync)
             .Produces<RecentDaysDto>()
             .ProducesValidationProblem();
        return app;
    }

    internal static async Task<IResult> GetOnThisDayAsync(
        string? date,
        ClaimsPrincipal user,
        [FromServices] IFlashbackReader reader,
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
            return InvalidDate(nameof(date));

        var years = await reader.ReadDayAsync(userId, day, ct);
        return Results.Ok(new OnThisDayDto(
            LocalDatePattern.Iso.Format(day),
            [.. years.Select(year => new FlashbackYearDto(year.Year, [.. year.Memories.Select(AssetDtoMapper.ToDto)]))]));
    }

    internal static async Task<IResult> GetRecentDaysAsync(
        string? before,
        string? since,
        string? limit,
        ClaimsPrincipal user,
        [FromServices] IFlashbackReader reader,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.Unauthorized();
        if (Date(before) is not { Success: true } end)
            return InvalidDate(nameof(before));
        var start = Date(since);
        if (start is { Success: false })
            return InvalidDate(nameof(since));
        if (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out var most) || most is < 1 or > IFlashbackReader.MostRecentDays)
        {
            return ApiProblems.FieldProblem(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidLimit,
                nameof(limit),
                $"limit must be a whole number from 1 to {IFlashbackReader.MostRecentDays}.");
        }

        var days = await reader.ReadRecentDaysAsync(userId, end.Value, start?.Value, most, ct);
        return Results.Ok(new RecentDaysDto(
            [.. days.Select(day => new RecentDayDto(LocalDatePattern.Iso.Format(day.Date), day.Year, AssetDtoMapper.ToDto(day.Memory)))]));
    }

    private static ParseResult<LocalDate>? Date(string? text)
        => text is null ? null : LocalDatePattern.Iso.Parse(text);

    private static IResult InvalidDate(string field)
        => ApiProblems.FieldProblem(
            StatusCodes.Status400BadRequest,
            ErrorCodes.InvalidDate,
            field,
            $"{field} must be a calendar date in the form YYYY-MM-DD.");
}
