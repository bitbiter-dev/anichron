using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

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

    void Add(MediaAsset asset);
    void AddProxyFile(ProxyFile proxyFile);
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

    public void Add(MediaAsset asset)
        => db.MediaAssets.Add(asset);

    public void AddProxyFile(ProxyFile proxyFile)
        => db.ProxyFiles.Add(proxyFile);
}
