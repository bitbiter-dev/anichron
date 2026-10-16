using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public interface IMediaAssetReadRepository
{
    Task<MediaAsset?> FindVisibleAsync(Guid assetId, Guid userId, CancellationToken ct);
}

public sealed class EfMediaAssetReadRepository(AnichronDbContext db) : IMediaAssetReadRepository
{
    public Task<MediaAsset?> FindVisibleAsync(Guid assetId, Guid userId, CancellationToken ct)
        => db.MediaAssets.AsNoTracking().VisibleTo(userId).FirstOrDefaultAsync(a => a.Id == assetId, ct);
}
