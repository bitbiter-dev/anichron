using Anichron.Core.Data.Repository;

namespace Anichron.API.Endpoints;

internal static class Bursts
{
    internal static IReadOnlyList<AssetDto> Members(Guid burstId, IReadOnlyList<AssetView> members)
        => [.. OldestFirst(members)
            .Select((member, index) => AssetDtoMapper.ToDto(
                member, new AssetBurstDto(burstId, IsCover: index == 0, members.Count)))];

    private static List<AssetView> OldestFirst(IEnumerable<AssetView> views)
        => [.. views.OrderBy(v => v.DateCaptured).ThenBy(v => v.Id)];
}
