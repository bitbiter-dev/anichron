using Anichron.Core.Data.Repository;

namespace Anichron.API.Endpoints;

public sealed record FlashbackYearDto(int Year, IReadOnlyList<AssetDto> Assets);

internal static class FlashbackYears
{
    internal static IReadOnlyList<FlashbackYearDto> Group(IEnumerable<AssetView> views)
        => [.. views
            .GroupBy(v => v.Year)
            .OrderByDescending(year => year.Key)
            .Select(year => new FlashbackYearDto(
                year.Key,
                [.. year
                    .OrderByDescending(v => v.IsStarred)
                    .ThenBy(v => v.DateCaptured)
                    .ThenBy(v => v.Id)
                    .Select(AssetDtoMapper.ToDto)]))];
}
