using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record AssetView(
    AssetMedia Media,
    int Year,
    int Month,
    int Day,
    LocalDateTime DateCaptured,
    bool IsStarred,
    bool IsLiked,
    Guid? BurstId,
    Instant? HiddenAt)
{
    public Guid Id => Media.Id;
}

public enum AssetSet
{
    Shown = 0,
    Hidden = 1,
}

public sealed class InteractedAsset
{
    public required MediaAsset Asset { get; init; }
    public AssetInteraction? Interaction { get; init; }
}

/// <summary>
/// Narrows the assets an <see cref="IAssetViewReader"/> returns from the set it declares, filtering on entity
/// columns only, and may order and page the caller's rows.
/// </summary>
public interface IAssetSelection
{
    AssetSet Set { get; }

    IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets);

    IQueryable<InteractedAsset> Page(IQueryable<InteractedAsset> rows) => rows;
}

public sealed record AssetsById(IReadOnlyCollection<Guid> Ids) : IAssetSelection
{
    public AssetSet Set => AssetSet.Shown;

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets)
        => assets.Where(a => Ids.Contains(a.Id));
}

public sealed record BurstMembers(Guid BurstId) : IAssetSelection
{
    public AssetSet Set => AssetSet.Shown;

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets)
        => assets.Where(a => a.BurstId == BurstId);
}

/// <summary>The caller's hidden assets, newest hidden first, as one page after a cursor.</summary>
/// <param name="After">The last row of the previous page, or <c>null</c> for the first page.</param>
/// <param name="Take">The most rows to read.</param>
public sealed record HiddenAssets(AssetCursor? After, int Take) : IAssetSelection
{
    public AssetSet Set => AssetSet.Hidden;

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets;

    public IQueryable<InteractedAsset> Page(IQueryable<InteractedAsset> rows)
    {
        var after = After is { } cursor
            ? rows.Where(r => r.Interaction!.HiddenAt < cursor.At
                || (r.Interaction.HiddenAt == cursor.At && r.Asset.Id.CompareTo(cursor.AssetId) < 0))
            : rows;
        return after.OrderByDescending(r => r.Interaction!.HiddenAt).ThenByDescending(r => r.Asset.Id).Take(Take);
    }
}

public static class HiddenPages
{
    /// <summary>The caller's hidden assets as one page after a cursor, newest hidden first.</summary>
    /// <param name="reader">Reads the page's views.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="after">The last row of the previous page, or <c>null</c> for the first page.</param>
    /// <param name="size">The most assets on the page.</param>
    /// <param name="ct">Cancels the query.</param>
    public static Task<AssetPage<AssetView>> ReadHiddenPageAsync(
        this IAssetViewReader reader, Guid userId, AssetCursor? after, int size, CancellationToken ct)
        => AssetPage.ReadAsync(
            size,
            take => reader.ReadAsync(userId, new HiddenAssets(after, take), ct),
            view => new AssetCursor(view.HiddenAt!.Value, view.Id));
}

/// <summary>
/// Reads the caller's visible assets in the set a selection declares and matching it, with the caller's own
/// interaction state, in one query.
/// </summary>
public interface IAssetViewReader
{
    Task<IReadOnlyList<AssetView>> ReadAsync(Guid userId, IAssetSelection selection, CancellationToken ct);
}

public sealed class EfAssetViewReader(AnichronDbContext db) : IAssetViewReader
{
    public async Task<IReadOnlyList<AssetView>> ReadAsync(Guid userId, IAssetSelection selection, CancellationToken ct)
        => await Query(db, userId, selection).ToListAsync(ct);

    internal static IQueryable<MediaAsset> Listed(AnichronDbContext db, Guid userId, AssetSet set)
    {
        var visible = db.MediaAssets.VisibleTo(userId);
        var inSet = set == AssetSet.Hidden ? visible.HiddenFor(userId) : visible.NotHiddenFor(userId);
        return inSet.NotPairedMovOf(visible).Where(a => a.DateCaptured != null);
    }

    internal static IQueryable<AssetView> Query(AnichronDbContext db, Guid userId, IAssetSelection selection)
    {
        var rows = from a in selection.Apply(Listed(db, userId, selection.Set))
                   join i in db.Interactions.Where(i => i.UserId == userId) on a.Id equals i.AssetId into interactions
                   from i in interactions.DefaultIfEmpty()
                   select new InteractedAsset { Asset = a, Interaction = i };
        return selection.Page(rows).Select(AssetMedia.Into<InteractedAsset, AssetView>(
            r => r.Asset,
            (r, media) => new AssetView(
                media,
                r.Asset.Year!.Value,
                r.Asset.Month!.Value,
                r.Asset.Day!.Value,
                r.Asset.DateCaptured!.Value,
                r.Interaction != null && r.Interaction.IsStarred,
                r.Interaction != null && r.Interaction.IsLiked,
                r.Asset.BurstId,
                r.Interaction!.HiddenAt)));
    }
}
