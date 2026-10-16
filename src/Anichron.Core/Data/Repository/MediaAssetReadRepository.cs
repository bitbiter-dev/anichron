using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public static class MediaAssetVisibility
{
    public static IQueryable<MediaAsset> VisibleTo(this IQueryable<MediaAsset> assets, Guid userId)
        => assets.Where(a => a.StorageConfig.UserId == userId);
}

public interface IMediaAssetReadRepository
{
    Task<MediaAsset?> FindVisibleAsync(Guid assetId, Guid userId, CancellationToken ct);
}

public sealed class EfMediaAssetReadRepository(AnichronDbContext db) : IMediaAssetReadRepository
{
    public Task<MediaAsset?> FindVisibleAsync(Guid assetId, Guid userId, CancellationToken ct)
        => db.MediaAssets.VisibleTo(userId).FirstOrDefaultAsync(a => a.Id == assetId, ct);
}
