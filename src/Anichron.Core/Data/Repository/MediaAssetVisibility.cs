using Anichron.Core.Domain;

namespace Anichron.Core.Data.Repository;

public static class MediaAssetVisibility
{
    public static IQueryable<MediaAsset> VisibleTo(this IQueryable<MediaAsset> assets, Guid userId)
        => assets.Where(a => a.StorageConfig.UserId == userId && a.StorageConfig.IsActive && !a.IsSoftDeleted);

    public static IQueryable<MediaAsset> NotHiddenFor(this IQueryable<MediaAsset> assets, Guid userId)
        => assets.Where(a => !a.Interactions.Any(i => i.UserId == userId && i.IsHidden));
}
