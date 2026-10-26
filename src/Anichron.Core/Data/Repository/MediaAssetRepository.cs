using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record ContentLocation(Guid UserId, string ContentHash, Guid StorageConfigId, string FilePath);

public sealed record AssetPath(Guid Id, string FilePath);

public interface IMediaAssetRepository
{
    Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct);

    /// <summary>The config's rows that match, each with its proxy rows.</summary>
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

    /// <summary>Soft-deletes the rows among <paramref name="ids"/> that are still active.</summary>
    /// <param name="ids">The rows to soft-delete.</param>
    /// <param name="ct">Cancels the update.</param>
    /// <returns>The number of rows soft-deleted.</returns>
    Task<int> SoftDeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Loads the asset's metadata row, even when the asset is soft-deleted.</summary>
    /// <param name="asset">A tracked asset.</param>
    /// <param name="ct">Cancels the query.</param>
    Task LoadMetadataAsync(MediaAsset asset, CancellationToken ct);

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
        => await HashesOrPathsQuery(db, storageConfigId, contentHashes, filePaths)
            .Include(a => a.ProxyFiles)
            .ToListAsync(ct);

    internal static IQueryable<MediaAsset> HashesOrPathsQuery(
        AnichronDbContext db,
        Guid storageConfigId,
        IReadOnlyCollection<string> contentHashes,
        IReadOnlyCollection<string> filePaths)
        => db.MediaAssets
            .IgnoreQueryFilters()
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
            .Select(a => new AssetPath(a.Id, a.FilePath));

    public Task LoadMetadataAsync(MediaAsset asset, CancellationToken ct)
        => db.Entry(asset).Reference(a => a.Metadata).Query().IgnoreQueryFilters().LoadAsync(ct);

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
