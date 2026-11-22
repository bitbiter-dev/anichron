using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record AssetView(
    Guid Id,
    string ContentHash,
    MediaType MediaType,
    int Year,
    int Month,
    int Day,
    LocalDateTime DateCaptured,
    int? Width,
    int? Height,
    int? Orientation,
    int? DurationInSeconds,
    string? Blurhash,
    bool HasThumbnail,
    bool HasPreview,
    bool HasCompactPreview,
    bool HasVideo,
    Guid? PairedMovId,
    string? PairedMovContentHash,
    bool PairedMovHasVideo,
    bool IsStarred,
    bool IsLiked,
    Guid? BurstId,
    Instant? HiddenAt);

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

public sealed record OnThisDay(LocalDate Date) : IAssetSelection
{
    public AssetSet Set => AssetSet.Shown;

    private int LastDay
        => Date is { Month: 2, Day: 28 } && !CalendarSystem.Iso.IsLeapYear(Date.Year) ? 29 : Date.Day;

    public bool FallsOnTheDay(AssetView view)
        => view.Month == Date.Month && view.Day >= Date.Day && view.Day <= LastDay;

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets)
    {
        var lastDay = LastDay;
        var onTheDay = assets.Where(a => a.Month == Date.Month && a.Day >= Date.Day && a.Day <= lastDay && a.Year < Date.Year);
        return onTheDay.Union(
            from d in onTheDay
            where d.BurstId != null
            from a in assets
            where a.BurstId == d.BurstId
            select a);
    }
}

public sealed record BurstMembers(Guid BurstId) : IAssetSelection
{
    public AssetSet Set => AssetSet.Shown;

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets)
        => assets.Where(a => a.BurstId == BurstId);
}

public sealed record HiddenCursor(Instant HiddenAt, Guid AssetId);

/// <summary>The caller's hidden assets, newest hidden first, as one page after a cursor.</summary>
/// <param name="After">The last row of the previous page, or <c>null</c> for the first page.</param>
/// <param name="Take">The most rows to read.</param>
public sealed record HiddenAssets(HiddenCursor? After, int Take) : IAssetSelection
{
    public AssetSet Set => AssetSet.Hidden;

    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets) => assets;

    public IQueryable<InteractedAsset> Page(IQueryable<InteractedAsset> rows)
    {
        var after = After is { } cursor
            ? rows.Where(r => r.Interaction!.HiddenAt < cursor.HiddenAt
                || (r.Interaction.HiddenAt == cursor.HiddenAt && r.Asset.Id.CompareTo(cursor.AssetId) < 0))
            : rows;
        return after.OrderByDescending(r => r.Interaction!.HiddenAt).ThenByDescending(r => r.Asset.Id).Take(Take);
    }
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

    internal static IQueryable<AssetView> Query(AnichronDbContext db, Guid userId, IAssetSelection selection)
    {
        var visible = db.MediaAssets.VisibleTo(userId);
        var set = selection.Set == AssetSet.Hidden ? visible.HiddenFor(userId) : visible.NotHiddenFor(userId);
        var rows = from a in selection.Apply(set.NotPairedMovOf(visible).Where(a => a.DateCaptured != null))
                   join i in db.Interactions.Where(i => i.UserId == userId) on a.Id equals i.AssetId into interactions
                   from i in interactions.DefaultIfEmpty()
                   select new InteractedAsset { Asset = a, Interaction = i };
        return from r in selection.Page(rows)
               let a = r.Asset
               let i = r.Interaction
               select new AssetView(
                   a.Id,
                   a.ContentHash,
                   a.MediaType,
                   a.Year!.Value,
                   a.Month!.Value,
                   a.Day!.Value,
                   a.DateCaptured!.Value,
                   a.Metadata!.Description.Width,
                   a.Metadata.Description.Height,
                   a.Metadata.Description.Orientation,
                   a.Metadata.Description.DurationInSeconds,
                   a.Metadata.Blurhash,
                   a.ProxyFiles.Any(p => p.ProxyType == ProxyType.Thumbnail),
                   a.ProxyFiles.Any(p => p.ProxyType == ProxyType.FullPreview),
                   a.ProxyFiles.Any(p => p.ProxyType == ProxyType.CompactPreview),
                   a.ProxyFiles.Any(p => p.ProxyType == ProxyType.WebVideo),
                   a.PairedAsset!.Id,
                   a.PairedAsset.ContentHash,
                   a.PairedAsset.ProxyFiles.Any(p => p.ProxyType == ProxyType.WebVideo),
                   i != null && i.IsStarred,
                   i != null && i.IsLiked,
                   a.BurstId,
                   i.HiddenAt);
    }
}
