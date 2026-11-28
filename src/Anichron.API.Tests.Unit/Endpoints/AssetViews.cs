using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

internal static class AssetViews
{
    private static AssetMedia Picture(Guid id) => new(
        id, "0123456789abcdef", MediaType.Image,
        Width: null, Height: null, Orientation: null, DurationInSeconds: null, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasCompactPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovContentHash: null, PairedMovHasVideo: false);

    public static AssetView Image(Guid id, LocalDateTime captured) => new(
        Picture(id), captured.Year, captured.Month, captured.Day, captured,
        IsStarred: false, IsLiked: false, BurstId: null, HiddenAt: null);

    public static SoftDeletedAssetView SoftDeleted(Guid id, Instant lastSeenOnNas) => new(
        Picture(id), 2019, 11, 12,
        FilePath: "2019/IMG_0001.HEIC", LastSeenOnNas: lastSeenOnNas, Reason: SoftDeleteReason.Missing);

    public static AssetView WithMedia(this AssetView view, Func<AssetMedia, AssetMedia> change)
        => view with { Media = change(view.Media) };

    public static SoftDeletedAssetView WithMedia(this SoftDeletedAssetView view, Func<AssetMedia, AssetMedia> change)
        => view with { Media = change(view.Media) };
}
