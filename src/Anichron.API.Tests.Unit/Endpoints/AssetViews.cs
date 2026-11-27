using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

internal static class AssetViews
{
    public static AssetView Image(Guid id, LocalDateTime captured) => new(
        id, "0123456789abcdef", MediaType.Image, captured.Year, captured.Month, captured.Day, captured,
        Width: null, Height: null, Orientation: null, DurationInSeconds: null, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasCompactPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovContentHash: null, PairedMovHasVideo: false,
        IsStarred: false, IsLiked: false, BurstId: null, HiddenAt: null);

    public static SoftDeletedAssetView SoftDeleted(Guid id, Instant lastSeenOnNas) => new(
        id, "0123456789abcdef", MediaType.Image, 2019, 11, 12,
        Width: null, Height: null, Orientation: null, DurationInSeconds: null, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasCompactPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovContentHash: null, PairedMovHasVideo: false,
        FilePath: "2019/IMG_0001.HEIC", LastSeenOnNas: lastSeenOnNas, Reason: SoftDeleteReason.Missing);
}
