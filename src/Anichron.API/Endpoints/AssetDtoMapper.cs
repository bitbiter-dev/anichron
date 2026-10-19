using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using System.Diagnostics;

namespace Anichron.API.Endpoints;

internal static class AssetDtoMapper
{
    internal static AssetDto ToDto(AssetView view)
    {
        var swapsAxes = view.Orientation is { } orientation && ExifOrientation.SwapsAxes(orientation);

        return new AssetDto(
            view.Id,
            PublishedMediaType(view.MediaType),
            view.Year,
            view.Month,
            view.Day,
            swapsAxes ? view.Height : view.Width,
            swapsAxes ? view.Width : view.Height,
            view.MediaType == MediaType.Video ? view.DurationInSeconds : null,
            new AssetProxyUrls(
                view.HasThumbnail ? ProxyKinds.UrlOf(view.Id, ProxyType.Thumbnail) : null,
                view.HasPreview ? ProxyKinds.UrlOf(view.Id, ProxyType.FullPreview) : null,
                view.HasVideo ? ProxyKinds.UrlOf(view.Id, ProxyType.WebVideo) : null),
            view.Blurhash,
            view is { PairedMovHasVideo: true, PairedMovId: { } movId }
                ? ProxyKinds.UrlOf(movId, ProxyType.WebVideo)
                : null,
            new AssetInteractionDto(view.IsStarred, view.IsLiked, Hidden: false, FlashbackWeight.For(view.IsLiked)));
    }

    internal static AssetInteractionDto ToDto(InteractionState state)
        => new(state.Starred, state.Liked, state.Hidden, FlashbackWeight.For(state.Liked));

    private static string PublishedMediaType(MediaType type)
        => type switch
        {
            MediaType.Image => "Image",
            MediaType.Video => "Video",
            MediaType.LivePhoto => "LivePhoto",
            _ => throw new UnreachableException($"MediaType {type} has no published value."),
        };
}
