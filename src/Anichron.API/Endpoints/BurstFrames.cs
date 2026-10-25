using Anichron.Core.Data.Repository;

namespace Anichron.API.Endpoints;

internal static class BurstFrames
{
    internal static IReadOnlyList<AssetDto> List(Guid burstId, IReadOnlyCollection<AssetView> members)
        => [.. OldestFirst(members)
            .Select((member, index) => AssetDtoMapper.ToDto(
                member, new AssetBurstDto(burstId, IsCover: index == 0, members.Count)))];

    internal static (AssetView Cover, AssetBurstDto Burst) Cover(Guid burstId, IReadOnlyCollection<AssetView> members)
    {
        var cover = OldestFirst(members)[0] with
        {
            IsStarred = members.Any(m => m.IsStarred),
            IsLiked = members.Any(m => m.IsLiked),
        };
        return (cover, new AssetBurstDto(burstId, IsCover: true, members.Count));
    }

    private static List<AssetView> OldestFirst(IEnumerable<AssetView> views)
        => [.. views.OrderBy(v => v.DateCaptured).ThenBy(v => v.Id)];
}
