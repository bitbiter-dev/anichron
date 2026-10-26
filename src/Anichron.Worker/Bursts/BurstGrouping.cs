using Anichron.Core.Domain;

namespace Anichron.Worker.Bursts;

/// <summary>What a photo's join changes.</summary>
/// <param name="Members">Every row that ends up in the burst, the photo included.</param>
/// <param name="Target">The burst the members join; null when a new one is created.</param>
/// <param name="Merged">The other bursts, whose members move into the target.</param>
/// <param name="Cover">The earliest active member.</param>
internal sealed record BurstJoin(IReadOnlySet<MediaAsset> Members, Burst? Target, IReadOnlyList<Burst> Merged, MediaAsset Cover);

/// <summary>Burst detection's grouping rule, on rows tracked by one context, so a row is one instance.</summary>
internal static class BurstGrouping
{
    /// <summary>
    /// Joins the photo with every labelled neighbour, then with each similar neighbour in order unless
    /// its burst would make the active members span more than <paramref name="spanSeconds"/>.
    /// </summary>
    /// <param name="photo">The photo being checked.</param>
    /// <param name="labelled">The candidates the camera gave the photo's burst label.</param>
    /// <param name="similar">The candidates whose thumbnails matched, oldest first.</param>
    /// <param name="bursts">The neighbours' bursts, with every member.</param>
    /// <param name="spanSeconds">The longest time, in seconds, the active members may span.</param>
    /// <returns>Null when the photo joins nothing.</returns>
    public static BurstJoin? Join(
        MediaAsset photo,
        IReadOnlyCollection<MediaAsset> labelled,
        IReadOnlyList<MediaAsset> similar,
        IReadOnlyCollection<Burst> bursts,
        int spanSeconds)
    {
        IEnumerable<MediaAsset> GroupOf(MediaAsset asset)
            => bursts.FirstOrDefault(b => b.Id == asset.BurstId)?.Assets ?? [asset];

        HashSet<MediaAsset> members = [photo, .. labelled.SelectMany(GroupOf)];
        foreach (var neighbour in similar)
        {
            HashSet<MediaAsset> grown = [.. members, .. GroupOf(neighbour)];
            if (WithinSpan(grown, spanSeconds))
                members = grown;
        }

        if (members.Count == 1)
            return null;

        var included = bursts
            .Where(b => members.Any(m => m.BurstId == b.Id))
            .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id)
            .ToList();
        return new BurstJoin(members, included.FirstOrDefault(), [.. included.Skip(1)], Cover(members));
    }

    public static MediaAsset Cover(IEnumerable<MediaAsset> members)
        => members.Where(a => !a.IsSoftDeleted).MinBy(a => (a.DateCaptured, a.Id))!;

    private static bool WithinSpan(IEnumerable<MediaAsset> members, int spanSeconds)
    {
        var captured = members.Where(a => !a.IsSoftDeleted).Select(a => a.DateCaptured).ToList();
        return captured.Max() <= captured.Min().PlusSeconds(spanSeconds);
    }
}
