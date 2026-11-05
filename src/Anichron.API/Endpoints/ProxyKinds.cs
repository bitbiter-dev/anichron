using Anichron.API.Infrastructure;
using Anichron.Core.Domain;

namespace Anichron.API.Endpoints;

internal sealed record ProxyKind(ProxyType Type, string Slug, string ContentType);

internal static class ProxyKinds
{
    internal static IReadOnlyList<ProxyKind> All => Table();

    internal static ProxyKind? FromSlug(string slug)
        => All.FirstOrDefault(kind => kind.Slug == slug);

    internal static string UrlOf(Guid assetId, ProxyType type, string contentHash)
        => ApiPaths.Proxies.ProxyUrl(assetId, All.Single(kind => kind.Type == type).Slug, contentHash);

    private static ProxyKind[] Table()
    {
        return
        [
            new(ProxyType.Thumbnail, "thumbnail", "image/jpeg"),
            new(ProxyType.FullPreview, "preview", "image/jpeg"),
            new(ProxyType.WebVideo, "video", "video/mp4"),
        ];
    }
}
