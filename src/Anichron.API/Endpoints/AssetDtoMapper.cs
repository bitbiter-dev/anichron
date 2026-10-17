using Anichron.API.Infrastructure;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.API.Endpoints;

internal static class AssetDtoMapper
{
    internal const double DefaultDisplayWeight = 1.0;
    internal const double LikedDisplayWeight = 2.0;

    internal static AssetDto ToDto(AssetView view)
    {
        var quarterTurned = view.Orientation >= ExifOrientation.FirstQuarterTurn;

        return new AssetDto(
            view.Id,
            view.MediaType.ToString(),
            view.Year,
            view.Month,
            view.Day,
            quarterTurned ? view.Height : view.Width,
            quarterTurned ? view.Width : view.Height,
            view.MediaType == MediaType.Video ? view.DurationInSeconds : null,
            new AssetProxyUrls(
                view.HasThumbnail ? ApiPaths.Assets.ProxyUrl(view.Id, ApiPaths.Assets.ThumbnailProxy) : null,
                view.HasPreview ? ApiPaths.Assets.ProxyUrl(view.Id, ApiPaths.Assets.PreviewProxy) : null,
                view.HasVideo ? ApiPaths.Assets.ProxyUrl(view.Id, ApiPaths.Assets.VideoProxy) : null),
            view.Blurhash,
            view is { PairedMovHasVideo: true, PairedMovId: { } movId }
                ? ApiPaths.Assets.ProxyUrl(movId, ApiPaths.Assets.VideoProxy)
                : null,
            new AssetInteractionDto(view.IsStarred, view.IsLiked, Hidden: false, DisplayWeight(view.IsLiked)));
    }

    internal static double DisplayWeight(bool liked)
        => liked ? LikedDisplayWeight : DefaultDisplayWeight;
}
