using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record ContentLocation(Guid UserId, string ContentHash, Guid StorageConfigId, string FilePath);

public interface IMediaAssetRepository
{
    Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct);

    /// <summary>The config's rows that match, each with its proxy rows.</summary>
    /// <param name="storageConfigId">The config whose rows are searched.</param>
    /// <param name="contentHashes">Matches active rows only.</param>
    /// <param name="filePaths">Matches soft-deleted rows too, as the unique path index does.</param>
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

    Task LoadMetadataAsync(MediaAsset asset, CancellationToken ct);

    /// <summary>Clears the asset's burst-checked mark, if it has one, at the next save.</summary>
    /// <param name="assetId">The asset whose mark is cleared.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task RemoveBurstCheckAsync(Guid assetId, CancellationToken ct);

    void Add(MediaAsset asset);
    void AddProxyFiles(IEnumerable<ProxyFile> proxyFiles);
    void RemoveProxyFile(ProxyFile proxyFile);
}

public sealed class EfMediaAssetRepository(AnichronDbContext db) : IMediaAssetRepository
{
    public Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct)
        => db.MediaAssets.FirstOrDefaultAsync(
            a => a.ContentHash == contentHash && a.StorageConfigId == storageConfigId, ct);

    public async Task<IReadOnlyList<MediaAsset>> FindByHashesOrPathsAsync(
        Guid storageConfigId,
        IReadOnlyCollection<string> contentHashes,
        IReadOnlyCollection<string> filePaths,
        CancellationToken ct)
        => await db.MediaAssets
            .IgnoreQueryFilters()
            .Include(a => a.ProxyFiles)
            .Where(a => a.StorageConfigId == storageConfigId
                && ((!a.IsSoftDeleted && contentHashes.Contains(a.ContentHash)) || filePaths.Contains(a.FilePath)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContentLocation>> FindSharedContentAsync(
        IReadOnlyCollection<Guid> storageConfigIds, CancellationToken ct)
        => await SharedContentQuery(db, storageConfigIds).ToListAsync(ct);

    internal static IQueryable<ContentLocation> SharedContentQuery(
        AnichronDbContext db, IReadOnlyCollection<Guid> storageConfigIds)
    {
        var inScope = db.MediaAssets.Where(a => storageConfigIds.Contains(a.StorageConfigId));
        var shared = inScope
            .GroupBy(a => new { a.StorageConfig.UserId, a.ContentHash })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        return inScope
            .Where(a => shared.Any(k => k.UserId == a.StorageConfig.UserId && k.ContentHash == a.ContentHash))
            .Select(a => new ContentLocation(a.StorageConfig.UserId, a.ContentHash, a.StorageConfigId, a.FilePath));
    }

    public Task LoadMetadataAsync(MediaAsset asset, CancellationToken ct)
        => db.Entry(asset).Reference(a => a.Metadata).LoadAsync(ct);

    public async Task RemoveBurstCheckAsync(Guid assetId, CancellationToken ct)
    {
        if (await db.BurstChecks.FindAsync([assetId], ct) is { } check)
            db.BurstChecks.Remove(check);
    }

    public void Add(MediaAsset asset)
        => db.MediaAssets.Add(asset);

    public void AddProxyFiles(IEnumerable<ProxyFile> proxyFiles)
        => db.ProxyFiles.AddRange(proxyFiles);

    public void RemoveProxyFile(ProxyFile proxyFile)
        => db.ProxyFiles.Remove(proxyFile);
}
