using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Tests.Unit.Endpoints;

internal static class AssetViews
{
    public static AssetView Image(Guid id, LocalDateTime captured) => new(
        id, MediaType.Image, captured.Year, captured.Month, captured.Day, captured,
        Width: null, Height: null, Orientation: null, DurationInSeconds: null, Blurhash: null,
        HasThumbnail: false, HasPreview: false, HasVideo: false,
        PairedMovId: null, PairedMovHasVideo: false,
        IsStarred: false, IsLiked: false);
}
