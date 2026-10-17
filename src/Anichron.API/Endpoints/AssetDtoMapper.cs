using Anichron.API.Infrastructure;
using Anichron.Core.Domain;

namespace Anichron.API.Endpoints;

internal static class AssetDtoMapper
{
    internal const double DefaultDisplayWeight = 1.0;
    internal const double LikedDisplayWeight = 2.0;

    internal static AssetDto ToDto(MediaAsset asset, AssetInteraction? interaction)
    {
        var metadata = asset.Metadata;
        var quarterTurned = metadata?.Orientation >= ExifOrientation.FirstQuarterTurn;

        return new AssetDto(
            asset.Id,
            asset.MediaType.ToString(),
            asset.Year,
            asset.Month,
            asset.Day,
            quarterTurned ? metadata?.Height : metadata?.Width,
            quarterTurned ? metadata?.Width : metadata?.Height,
            asset.MediaType == MediaType.Video ? metadata?.DurationInSeconds : null,
            new AssetProxyUrls(
                ProxyUrl(asset, ProxyType.Thumbnail, ApiPaths.Assets.ThumbnailProxy),
                ProxyUrl(asset, ProxyType.FullPreview, ApiPaths.Assets.PreviewProxy),
                ProxyUrl(asset, ProxyType.WebVideo, ApiPaths.Assets.VideoProxy)),
            metadata?.Blurhash,
            asset.PairedAsset is { } pairedMov
                ? ProxyUrl(pairedMov, ProxyType.WebVideo, ApiPaths.Assets.VideoProxy)
                : null,
            ToDto(interaction));
    }

    internal static AssetInteractionDto ToDto(AssetInteraction? interaction)
        => interaction is null
            ? new AssetInteractionDto(false, false, false, DefaultDisplayWeight)
            : new AssetInteractionDto(
                interaction.IsStarred,
                interaction.IsLiked,
                interaction.IsHidden,
                DisplayWeight(interaction.IsLiked));

    internal static double DisplayWeight(bool liked)
        => liked ? LikedDisplayWeight : DefaultDisplayWeight;

    private static string? ProxyUrl(MediaAsset asset, ProxyType type, string slug)
        => asset.ProxyFiles.Any(proxy => proxy.ProxyType == type)
            ? ApiPaths.Assets.ProxyUrl(asset.Id, slug)
            : null;
}
