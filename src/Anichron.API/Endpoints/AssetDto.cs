namespace Anichron.API.Endpoints;

public sealed record AssetDto(
    Guid Id,
    string MediaType,
    int Year,
    int Month,
    int Day,
    int? Width,
    int? Height,
    int? DurationSeconds,
    AssetProxyUrls Proxies,
    string? Blurhash,
    string? LivePhotoVideoUrl,
    AssetInteractionDto Interaction,
    AssetBurstDto? Burst);

public sealed record AssetProxyUrls(string? Thumbnail, string? Preview, string? CompactPreview, string? Video);

public sealed record AssetBurstDto(Guid BurstId, bool IsCover, int Count);

public sealed record AssetInteractionDto(bool Starred, bool Liked, bool Hidden, double DisplayWeight);

public sealed record HiddenAssetDto(AssetDto Asset, string HiddenAt);

public sealed record HiddenAssetsPageDto(IReadOnlyList<HiddenAssetDto> Items, string? Next);

public sealed record SoftDeletedAssetDto(
    Guid Id,
    string MediaType,
    int? Year,
    int? Month,
    int? Day,
    int? Width,
    int? Height,
    int? DurationSeconds,
    AssetProxyUrls Proxies,
    string? Blurhash,
    string? LivePhotoVideoUrl,
    string Path,
    string LastSeenOnNas,
    string Reason);

public sealed record SoftDeletedAssetsPageDto(IReadOnlyList<SoftDeletedAssetDto> Items, string? Next, int Total);
