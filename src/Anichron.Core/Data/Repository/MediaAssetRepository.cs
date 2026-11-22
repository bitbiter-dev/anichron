using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record ContentLocation(Guid UserId, string ContentHash, Guid StorageConfigId, string FilePath);

/// <summary>An active row's path, with what the missing-file sweep needs to soft-delete a Live Photo as one.</summary>
/// <param name="Id">The row.</param>
/// <param name="FilePath">Its path, relative to the config's root.</param>
/// <param name="PairedAssetId">The video the row points at, when it is a Live Photo's photo.</param>
public sealed record AssetPath(Guid Id, string FilePath, Guid? PairedAssetId = null);

public interface IMediaAssetRepository
{
    Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct);

    /// <summary>The config's rows that match, tracked, each with its proxy rows.</summary>
    /// <param name="storageConfigId">The config whose rows are searched.</param>
    /// <param name="contentHashes">Matches active and soft-deleted rows.</param>
    /// <param name="filePaths">Matches active and soft-deleted rows, as the unique path index does.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> FindByHashesOrPathsAsync(
        Guid storageConfigId,
        IReadOnlyCollection<string> contentHashes,
        IReadOnlyCollection<string> filePaths,
        CancellationToken ct);

    /// <summary>Active rows whose content is also stored in another of the same user's configs.</summary>
    /// <param name="storageConfigIds">Only rows in these configs are compared.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<ContentLocation>> FindSharedContentAsync(IReadOnlyCollection<Guid> storageConfigIds, CancellationToken ct);

    Task<IReadOnlyList<AssetPath>> GetActivePathsAsync(Guid storageConfigId, CancellationToken ct);

    /// <summary>The tracked row with the id, else the active stored one.</summary>
    /// <param name="id">The row.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>The row, tracked; null when it is neither tracked nor active.</returns>
    Task<MediaAsset?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>The stored rows, active or soft-deleted, that point at the video.</summary>
    /// <param name="videoId">The video.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>The rows, tracked: a row already tracked comes back as that instance, with its current values.</returns>
    Task<IReadOnlyList<MediaAsset>> FindPhotosOfAsync(Guid videoId, CancellationToken ct);

    /// <summary>Soft-deletes the rows among <paramref name="ids"/> that are still active.</summary>
    /// <param name="ids">The rows to soft-delete.</param>
    /// <param name="ct">Cancels the update.</param>
    /// <returns>The number of rows soft-deleted.</returns>
    Task<int> SoftDeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Loads the asset's metadata row, even when the asset is soft-deleted.</summary>
    /// <param name="asset">A tracked asset.</param>
    /// <param name="ct">Cancels the query.</param>
    Task LoadMetadataAsync(MediaAsset asset, CancellationToken ct);

    /// <summary>
    /// The config's active assets with a <see cref="ProxyType.FullPreview"/> but no
    /// <see cref="ProxyType.CompactPreview"/>, tracked with their proxy rows, by id.
    /// </summary>
    /// <param name="storageConfigId">The config whose assets are loaded.</param>
    /// <param name="after">Only assets whose id comes after this one; null starts at the first.</param>
    /// <param name="count">At most this many.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> GetWithoutCompactPreviewAsync(Guid storageConfigId, Guid? after, int count, CancellationToken ct);

    /// <summary>How many assets <see cref="GetWithoutCompactPreviewAsync"/> would find with no limit.</summary>
    /// <param name="storageConfigId">The config whose assets are counted.</param>
    /// <param name="after">Only assets whose id comes after this one; null counts from the first.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<int> CountWithoutCompactPreviewAsync(Guid storageConfigId, Guid? after, CancellationToken ct);

    void Add(MediaAsset asset);
    void AddProxyFiles(IEnumerable<ProxyFile> proxyFiles);
    void RemoveProxyFile(ProxyFile proxyFile);
}

public sealed class EfMediaAssetRepository(AnichronDbContext db) : IMediaAssetRepository
{
    public Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct)
        => ActiveByHashQuery(db, contentHash, storageConfigId).FirstOrDefaultAsync(ct);

    internal static IQueryable<MediaAsset> ActiveByHashQuery(AnichronDbContext db, string contentHash, Guid storageConfigId)
        => db.MediaAssets
            .IgnoreQueryFilters()
            .Where(a => a.ContentHash == contentHash && a.StorageConfigId == storageConfigId && !a.IsSoftDeleted);

    public async Task<IReadOnlyList<MediaAsset>> FindByHashesOrPathsAsync(
        Guid storageConfigId,
        IReadOnlyCollection<string> contentHashes,
        IReadOnlyCollection<string> filePaths,
        CancellationToken ct)
        => await HashesOrPathsQuery(db, storageConfigId, contentHashes, filePaths).ToListAsync(ct);

    internal static IQueryable<MediaAsset> HashesOrPathsQuery(
        AnichronDbContext db,
        Guid storageConfigId,
        IReadOnlyCollection<string> contentHashes,
        IReadOnlyCollection<string> filePaths)
        => db.MediaAssets
            .IgnoreQueryFilters()
            .Include(a => a.ProxyFiles)
            .Where(a => a.StorageConfigId == storageConfigId
                && ((!a.IsSoftDeleted && contentHashes.Contains(a.ContentHash))
                    || (a.IsSoftDeleted && contentHashes.Contains(a.ContentHash))
                    || filePaths.Contains(a.FilePath)));

    public async Task<IReadOnlyList<ContentLocation>> FindSharedContentAsync(
        IReadOnlyCollection<Guid> storageConfigIds, CancellationToken ct)
        => await SharedContentQuery(db, storageConfigIds).ToListAsync(ct);

    internal static IQueryable<ContentLocation> SharedContentQuery(
        AnichronDbContext db, IReadOnlyCollection<Guid> storageConfigIds)
    {
        var inScope = db.MediaAssets
            .IgnoreQueryFilters()
            .Where(a => storageConfigIds.Contains(a.StorageConfigId) && !a.IsSoftDeleted);
        var shared = inScope
            .GroupBy(a => new { a.StorageConfig.UserId, a.ContentHash })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        return inScope
            .Where(a => shared.Any(k => k.UserId == a.StorageConfig.UserId && k.ContentHash == a.ContentHash))
            .Select(a => new ContentLocation(a.StorageConfig.UserId, a.ContentHash, a.StorageConfigId, a.FilePath));
    }

    public async Task<IReadOnlyList<AssetPath>> GetActivePathsAsync(Guid storageConfigId, CancellationToken ct)
        => await ActivePathsQuery(db, storageConfigId).ToListAsync(ct);

    internal static IQueryable<AssetPath> ActivePathsQuery(AnichronDbContext db, Guid storageConfigId)
        => db.MediaAssets
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.StorageConfigId == storageConfigId && !a.IsSoftDeleted)
            .Select(a => new AssetPath(a.Id, a.FilePath, a.PairedAssetId));

    public async Task<MediaAsset?> FindAsync(Guid id, CancellationToken ct)
        => await db.MediaAssets.FindAsync([id], ct);

    public async Task<IReadOnlyList<MediaAsset>> FindPhotosOfAsync(Guid videoId, CancellationToken ct)
        => await db.MediaAssets.IgnoreQueryFilters().Where(a => a.PairedAssetId == videoId).ToListAsync(ct);

    public Task LoadMetadataAsync(MediaAsset asset, CancellationToken ct)
        => db.Entry(asset).Reference(a => a.Metadata).Query().IgnoreQueryFilters().LoadAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> GetWithoutCompactPreviewAsync(
        Guid storageConfigId, Guid? after, int count, CancellationToken ct)
        => await WithoutCompactPreviewQuery(db, storageConfigId, after)
            .Include(a => a.ProxyFiles)
            .OrderBy(a => a.Id)
            .Take(count)
            .ToListAsync(ct);

    public Task<int> CountWithoutCompactPreviewAsync(Guid storageConfigId, Guid? after, CancellationToken ct)
        => WithoutCompactPreviewQuery(db, storageConfigId, after).CountAsync(ct);

    internal static IQueryable<MediaAsset> WithoutCompactPreviewQuery(AnichronDbContext db, Guid storageConfigId, Guid? after)
    {
        var pending = db.MediaAssets
            .IgnoreQueryFilters()
            .Where(a => a.StorageConfigId == storageConfigId && !a.IsSoftDeleted
                && a.ProxyFiles.Any(p => p.ProxyType == ProxyType.FullPreview)
                && !a.ProxyFiles.Any(p => p.ProxyType == ProxyType.CompactPreview));
        return after is { } id ? pending.Where(a => a.Id.CompareTo(id) > 0) : pending;
    }

    public void Add(MediaAsset asset)
        => db.MediaAssets.Add(asset);

    public void AddProxyFiles(IEnumerable<ProxyFile> proxyFiles)
        => db.ProxyFiles.AddRange(proxyFiles);

    public void RemoveProxyFile(ProxyFile proxyFile)
        => db.ProxyFiles.Remove(proxyFile);

    public Task<int> SoftDeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => SoftDeleteAsync(ids, batchSize: 1000, ct);

    internal static IQueryable<MediaAsset> StillActiveQuery(AnichronDbContext db, IReadOnlyCollection<Guid> ids)
        => db.MediaAssets
            .IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id) && !a.IsSoftDeleted);

    // Stryker disable all
    internal async Task<int> SoftDeleteAsync(IReadOnlyCollection<Guid> ids, int batchSize, CancellationToken ct)
    {
        var softDeleted = 0;
        foreach (var batch in ids.Chunk(batchSize))
        {
            softDeleted += await StillActiveQuery(db, batch)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsSoftDeleted, true), ct);
        }

        return softDeleted;
    }
}
