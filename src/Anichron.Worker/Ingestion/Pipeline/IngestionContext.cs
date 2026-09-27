using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Pipeline;

internal sealed class IngestionContext
{
    private readonly List<ProxyFile> proxyFiles = [];
    private readonly Lock proxyFilesLock = new();

    public required IngestionItem Item { get; init; }
    public required UserStorageConfig Config { get; init; }

    public required Guid AssetId { get; init; }

    public string? ContentHash { get; set; }
    public string? SecondaryHash { get; set; }
    public ExifData? Exif { get; set; }
    public MediaAsset? Asset { get; set; }

    // Records what this attempt has actually put on disk: each proxy registers itself the
    // moment it is renamed into place, so compensation on failure can delete precisely those
    // files. Image proxies are generated concurrently, hence the lock and the snapshot.
    public IReadOnlyList<ProxyFile> ProxyFiles
    {
        get
        {
            lock (proxyFilesLock)
                return [.. proxyFiles];
        }
    }

    public void AddProxyFile(ProxyFile proxyFile)
    {
        lock (proxyFilesLock)
            proxyFiles.Add(proxyFile);
    }
}
