using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Ingestion.Pipeline;

internal sealed class IngestionContext
{
    private readonly List<ProxyFile> proxyFiles = [];
    private readonly Lock proxyFilesLock = new();

    public required IngestionItem Item { get; init; }
    public required UserStorageConfig Config { get; init; }

    public required Guid AttemptId { get; init; }

    public Guid AssetId { get; set; }

    public string? ContentHash { get; set; }
    public string? SecondaryHash { get; set; }
    public IngestionClassification? Classification { get; set; }
    public ExifData? Exif { get; set; }
    public string? Blurhash { get; set; }
    public MediaAsset? Asset { get; set; }

    // Image proxies register concurrently.
    public IReadOnlyList<ProxyFile> ProxyFiles
    {
        get
        {
            lock (proxyFilesLock)
                return [.. proxyFiles];
        }
    }

    public IReadOnlyList<ProxyFile> ProxyFilesOf(Guid ownerAssetId)
    {
        lock (proxyFilesLock)
            return [.. proxyFiles.Where(proxy => proxy.AssetId == ownerAssetId)];
    }

    public void AddProxyFile(ProxyFile proxyFile)
    {
        lock (proxyFilesLock)
            proxyFiles.Add(proxyFile);
    }

    public void ReleaseProxyFiles(Guid ownerAssetId)
    {
        lock (proxyFilesLock)
            proxyFiles.RemoveAll(proxy => proxy.AssetId == ownerAssetId);
    }
}
