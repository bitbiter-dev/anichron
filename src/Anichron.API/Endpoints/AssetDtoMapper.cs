using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using System.Diagnostics;

namespace Anichron.API.Endpoints;

internal static class AssetDtoMapper
{
    private sealed record Media(
        string MediaType, int? Width, int? Height, int? DurationSeconds, AssetProxyUrls Proxies, string? LivePhotoVideoUrl);

    internal static AssetDto ToDto(AssetView view, AssetBurstDto? burst)
    {
        var media = MediaOf(view);
        return new AssetDto(
            view.Id,
            media.MediaType,
            view.Year,
            view.Month,
            view.Day,
            media.Width,
            media.Height,
            media.DurationSeconds,
            media.Proxies,
            view.Blurhash,
            media.LivePhotoVideoUrl,
            ToDto(new InteractionState(view.IsStarred, view.IsLiked, view.HiddenAt is not null)),
            burst);
    }

    internal static AssetDto ToDto(FlashbackMemory memory)
        => ToDto(memory.View, memory.Burst is { } burst ? new AssetBurstDto(burst.BurstId, IsCover: true, burst.Count) : null);

    internal static SoftDeletedAssetDto ToDto(SoftDeletedAssetView view)
    {
        var media = MediaOf(view);
        return new SoftDeletedAssetDto(
            view.Id,
            media.MediaType,
            view.Year,
            view.Month,
            view.Day,
            media.Width,
            media.Height,
            media.DurationSeconds,
            media.Proxies,
            view.Blurhash,
            media.LivePhotoVideoUrl,
            view.FilePath,
            AssetCursors.FormatInstant(view.LastSeenOnNas),
            view.Reason.ToString());
    }

    internal static AssetInteractionDto ToDto(InteractionState state)
        => new(state.Starred, state.Liked, state.Hidden, FlashbackWeight.For(state.Liked));

    private static Media MediaOf(IAssetMedia view)
    {
        var swapsAxes = view.Orientation is { } orientation && ExifOrientation.SwapsAxes(orientation);
        var liveVideoUrl = view is { PairedMovHasVideo: true, PairedMovId: { } movId, PairedMovContentHash: { } movHash }
            ? ProxyKinds.UrlOf(movId, ProxyType.WebVideo, movHash)
            : null;

        return new Media(
            Published(view.MediaType, liveVideoUrl is not null).ToString(),
            swapsAxes ? view.Height : view.Width,
            swapsAxes ? view.Width : view.Height,
            view.MediaType == MediaType.Video ? view.DurationInSeconds : null,
            new AssetProxyUrls(
                view.HasThumbnail ? ProxyKinds.UrlOf(view.Id, ProxyType.Thumbnail, view.ContentHash) : null,
                view.HasPreview ? ProxyKinds.UrlOf(view.Id, ProxyType.FullPreview, view.ContentHash) : null,
                view.HasCompactPreview ? ProxyKinds.UrlOf(view.Id, ProxyType.CompactPreview, view.ContentHash) : null,
                view.HasVideo ? ProxyKinds.UrlOf(view.Id, ProxyType.WebVideo, view.ContentHash) : null),
            liveVideoUrl);
    }

    private static PublishedMediaType Published(MediaType type, bool playsLive)
        => type switch
        {
            MediaType.Image => PublishedMediaType.Image,
            MediaType.Video => PublishedMediaType.Video,
            MediaType.LivePhoto => playsLive ? PublishedMediaType.LivePhoto : PublishedMediaType.Image,
            _ => throw new UnreachableException($"MediaType {type} has no published value."),
        };
}
