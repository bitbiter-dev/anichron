using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Pipeline;

internal interface IWithHash
{ string? ContentHash { get; set; } }
internal interface IWithExif
{ ExifData? Exif { get; set; } }
internal interface IWithAsset
{ MediaAsset? Asset { get; set; } }
internal interface IWithProxies
{ List<ProxyFile> ProxyFiles { get; } }

internal sealed class IngestionContext : IWithHash, IWithExif, IWithAsset, IWithProxies
{
    public required IngestionItem Item { get; init; }
    public required UserStorageConfig Config { get; init; }

    public string? ContentHash { get; set; }
    public string? MovContentHash { get; set; }
    public ExifData? Exif { get; set; }
    public MediaAsset? Asset { get; set; }
    public List<ProxyFile> ProxyFiles { get; } = [];
}
