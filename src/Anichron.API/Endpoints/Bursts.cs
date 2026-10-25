using Anichron.Core.Data.Repository;

namespace Anichron.API.Endpoints;

internal sealed record AssetItem(AssetView View, AssetBurstDto? Burst);

internal static class Bursts
{
    internal static IReadOnlyList<AssetDto> Members(Guid burstId, IReadOnlyList<AssetView> members)
        => [.. OldestFirst(members)
            .Select((member, index) => AssetDtoMapper.ToDto(
                member, new AssetBurstDto(burstId, IsCover: index == 0, members.Count)))];

    internal static AssetItem Cover(Guid burstId, IReadOnlyCollection<AssetView> members)
    {
        var cover = OldestFirst(members)[0] with
        {
            IsStarred = members.Any(m => m.IsStarred),
            IsLiked = members.Any(m => m.IsLiked),
        };
        return new AssetItem(cover, new AssetBurstDto(burstId, IsCover: true, members.Count));
    }

    private static List<AssetView> OldestFirst(IEnumerable<AssetView> views)
        => [.. views.OrderBy(v => v.DateCaptured).ThenBy(v => v.Id)];
}
