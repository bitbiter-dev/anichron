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

public sealed record AssetProxyUrls(string? Thumbnail, string? Preview, string? Video);

public sealed record AssetBurstDto(Guid BurstId, bool IsCover, int Count);

public sealed record AssetInteractionDto(bool Starred, bool Liked, bool Hidden, double DisplayWeight);
