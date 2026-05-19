using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IMediaAssetRepository
{
    Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct);
    void Add(MediaAsset asset);
}

public sealed class EfMediaAssetRepository(AnichronDbContext db) : IMediaAssetRepository
{
    public Task<MediaAsset?> FindByHashAsync(string contentHash, Guid storageConfigId, CancellationToken ct)
        => db.MediaAssets.FirstOrDefaultAsync(
            a => a.ContentHash == contentHash && a.StorageConfigId == storageConfigId, ct);

    public void Add(MediaAsset asset)
        => db.MediaAssets.Add(asset);
}
