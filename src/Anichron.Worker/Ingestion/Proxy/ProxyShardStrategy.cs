namespace Anichron.Worker.Ingestion.Proxy;

internal interface IProxyDirectoryStrategy
{
    string GetDirectory(Guid storageConfigId, string contentHash);
}

internal sealed class TwoLevelHexShardStrategy : IProxyDirectoryStrategy
{
    // Derived from content rather than from the ingestion attempt, so a retry of a failed
    // file lands in the same directory and overwrites its own debris instead of stranding it.
    // Two hex chars of the content hash → 256 top-level buckets; prevents filesystem
    // directory-count blowup at large library sizes. The storage config id keeps byte-identical
    // files of different users apart — without it, removing one user's config would cascade
    // away the other user's proxies.
    public string GetDirectory(Guid storageConfigId, string contentHash)
        => $"{contentHash[..2]}/{storageConfigId:N}-{contentHash}";
}
