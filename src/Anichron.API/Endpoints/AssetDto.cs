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
    string? LivePhoto,
    AssetInteractionDto Interaction);

public sealed record AssetProxyUrls(string? Thumbnail, string? Preview, string? Video);

public sealed record AssetInteractionDto(bool Starred, bool Liked, bool Hidden, double DisplayWeight);
