using Anichron.Core.Domain;

namespace Anichron.Core.Data.Repository;

public static class MediaAssetVisibility
{
    public static IQueryable<MediaAsset> VisibleTo(this IQueryable<MediaAsset> assets, Guid userId)
        => assets.Where(a => a.StorageConfig.UserId == userId && a.StorageConfig.IsActive && !a.IsSoftDeleted);

    public static IQueryable<MediaAsset> NotHiddenFor(this IQueryable<MediaAsset> assets, Guid userId)
        => assets.Where(a => !a.Interactions.Any(i => i.UserId == userId && i.IsHidden));

    public static IQueryable<MediaAsset> NotPairedMovOf(this IQueryable<MediaAsset> assets, IQueryable<MediaAsset> livePhotos)
        => assets.Where(a => !livePhotos.Any(livePhoto => livePhoto.PairedAssetId == a.Id));
}
