using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Pipeline;

internal sealed class IngestionContext
{
    public required IngestionItem Item { get; init; }
    public required UserStorageConfig Config { get; init; }

    public string? ContentHash { get; set; }
    public string? SecondaryHash { get; set; }
    public ExifData? Exif { get; set; }
    public MediaAsset? Asset { get; set; }
    public List<ProxyFile> ProxyFiles { get; } = [];
}
