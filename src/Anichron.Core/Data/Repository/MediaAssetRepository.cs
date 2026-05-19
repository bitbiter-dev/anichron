using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IMediaAssetRepository
{
    Task<MediaAsset?> FindByHashAsync(string contentHash, CancellationToken ct);
    void Add(MediaAsset asset);
}

public sealed class EfMediaAssetRepository(AnichronDbContext db) : IMediaAssetRepository
{
    public Task<MediaAsset?> FindByHashAsync(string contentHash, CancellationToken ct)
        => db.MediaAssets.SingleOrDefaultAsync(a => a.ContentHash == contentHash, ct);

    public void Add(MediaAsset asset)
        => db.MediaAssets.Add(asset);
}
