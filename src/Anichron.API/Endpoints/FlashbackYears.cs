using Anichron.Core.Data.Repository;

namespace Anichron.API.Endpoints;

public sealed record FlashbackYearDto(int Year, IReadOnlyList<AssetDto> Assets);

internal static class FlashbackYears
{
    internal static IReadOnlyList<FlashbackYearDto> Group(IReadOnlyCollection<AssetView> views, OnThisDay day)
        => [.. Items(views, day)
            .GroupBy(item => item.View.Year)
            .OrderByDescending(year => year.Key)
            .Select(year => new FlashbackYearDto(
                year.Key,
                [.. year
                    .OrderByDescending(item => item.View.IsStarred)
                    .ThenBy(item => item.View.DateCaptured)
                    .ThenBy(item => item.View.Id)
                    .Select(item => AssetDtoMapper.ToDto(item.View, item.Burst))]))];

    private static IEnumerable<AssetItem> Items(IReadOnlyCollection<AssetView> views, OnThisDay day)
        => views
            .Where(v => v.BurstId is null)
            .Select(v => new AssetItem(v, null))
            .Concat(views
                .Where(v => v.BurstId is not null)
                .GroupBy(v => v.BurstId!.Value)
                .Select(burst => Bursts.Cover(burst.Key, [.. burst]))
                .Where(item => day.FallsOnTheDay(item.View)));
}
