using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;

namespace Anichron.Worker.Ingestion.Pipeline;

internal sealed class IngestionContext
{
    private readonly List<ProxyFile> proxyFiles = [];
    private readonly HashSet<ProxyFile> replacedFiles = [];
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

    /// <summary>Rows this item soft-deleted, counted once the change is made.</summary>
    public int SoftDeleted { get; private set; }

    /// <summary>Soft-deleted rows this item made active again, counted once the change is made.</summary>
    public int AutoRestored { get; private set; }

    public void CountSoftDeleted() => SoftDeleted++;

    public void CountAutoRestored() => AutoRestored++;

    /// <summary>Forgets the counts when the changes they counted were not saved.</summary>
    public void ForgetCounts() => (SoftDeleted, AutoRestored) = (0, 0);

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

    /// <summary>The registered proxies whose final file did not exist before this attempt wrote it.</summary>
    public IReadOnlyList<ProxyFile> CreatedProxyFiles
    {
        get
        {
            lock (proxyFilesLock)
                return [.. proxyFiles.Where(proxy => !replacedFiles.Contains(proxy))];
        }
    }

    public void AddProxyFile(ProxyFile proxyFile, bool replacedExistingFile = false)
    {
        lock (proxyFilesLock)
        {
            proxyFiles.Add(proxyFile);
            if (replacedExistingFile)
                replacedFiles.Add(proxyFile);
        }
    }

    public void ReleaseProxyFiles(Guid ownerAssetId)
    {
        lock (proxyFilesLock)
            proxyFiles.RemoveAll(proxy => proxy.AssetId == ownerAssetId);
    }
}
