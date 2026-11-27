using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record SoftDeletedAssetView(
    Guid Id,
    string ContentHash,
    MediaType MediaType,
    int? Year,
    int? Month,
    int? Day,
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
    string FilePath,
    Instant LastSeenOnNas,
    SoftDeleteReason Reason) : IAssetMedia;

/// <summary>A page of soft-deleted assets.</summary>
/// <param name="Items">The page's assets, newest last seen first.</param>
/// <param name="Next">The cursor of the next page, or <c>null</c> on the last page.</param>
/// <param name="Total">The assets on every page.</param>
public sealed record SoftDeletedPage(IReadOnlyList<SoftDeletedAssetView> Items, AssetCursor? Next, int Total);

/// <summary>An asset accepted for a purge.</summary>
/// <param name="AssetId">The asset.</param>
/// <param name="StorageConfigId">Its storage config, which scopes the purge.</param>
public sealed record PurgeTarget(Guid AssetId, Guid StorageConfigId);

/// <summary>
/// Reads the caller's soft-deleted assets in active configs: a Live Photo's video is folded into its photo, and a video
/// whose photo is active is left out.
/// </summary>
public interface ISoftDeletedAssetReader
{
    /// <summary>One page, newest <see cref="MediaAsset.LastSeenOnNas"/> first, then by id descending, with the list's total.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="after">The last row of the previous page, or <c>null</c> for the first page.</param>
    /// <param name="size">The most assets on the page.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<SoftDeletedPage> ReadPageAsync(Guid userId, AssetCursor? after, int size, CancellationToken ct);

    /// <summary>
    /// The requested ids that the list holds, plus the soft-deleted video of each accepted Live Photo. Any other id is
    /// left out, whoever it belongs to.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="ids">The requested asset ids.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<PurgeTarget>> ResolvePurgeAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

public sealed class EfSoftDeletedAssetReader(AnichronDbContext db) : ISoftDeletedAssetReader
{
    public async Task<SoftDeletedPage> ReadPageAsync(Guid userId, AssetCursor? after, int size, CancellationToken ct)
    {
        var views = await Query(db, userId, after, size + 1).ToListAsync(ct);
        var items = views.Take(size).ToList();
        var total = await Listed(db.MediaAssets.IgnoreQueryFilters(), userId).CountAsync(ct);
        return new SoftDeletedPage(
            items,
            views.Count > size ? new AssetCursor(items[^1].LastSeenOnNas, items[^1].Id) : null,
            total);
    }

    public async Task<IReadOnlyList<PurgeTarget>> ResolvePurgeAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => await PurgeQuery(db, userId, ids).ToListAsync(ct);

    internal static IQueryable<PurgeTarget> PurgeQuery(AnichronDbContext db, Guid userId, IReadOnlyCollection<Guid> ids)
    {
        var assets = db.MediaAssets.IgnoreQueryFilters();
        var requested = Listed(assets, userId).Where(a => ids.Contains(a.Id));
        return assets.SoftDeletedFor(userId)
            .Where(a => requested.Any(r => r.Id == a.Id || r.PairedAssetId == a.Id))
            .Select(a => new PurgeTarget(a.Id, a.StorageConfigId));
    }

    private static IQueryable<MediaAsset> Listed(IQueryable<MediaAsset> assets, Guid userId)
        => assets.SoftDeletedFor(userId).NotPairedMovOf(assets.OwnedBy(userId));

    internal static IQueryable<SoftDeletedAssetView> Query(AnichronDbContext db, Guid userId, AssetCursor? after, int take)
    {
        var listed = Listed(db.MediaAssets.IgnoreQueryFilters(), userId);
        var page = after is { } cursor
            ? listed.Where(a => a.LastSeenOnNas < cursor.At
                || (a.LastSeenOnNas == cursor.At && a.Id.CompareTo(cursor.AssetId) < 0))
            : listed;
        return page
            .OrderByDescending(a => a.LastSeenOnNas)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .Select(a => new SoftDeletedAssetView(
                a.Id,
                a.ContentHash,
                a.MediaType,
                a.Year,
                a.Month,
                a.Day,
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
                a.FilePath,
                a.LastSeenOnNas,
                a.SoftDeleteReason!.Value));
    }
}
