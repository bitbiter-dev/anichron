namespace Anichron.Worker.Ingestion.Proxy;

internal interface IProxyDirectoryStrategy
{
    string GetDirectory(Guid storageConfigId, string contentHash);

    /// <summary>The storage config whose directory holds a path relative to the proxy root, or null when it names none.</summary>
    /// <param name="relativePath">A directory <see cref="GetDirectory"/> built, or a path inside one.</param>
    Guid? StorageConfigOf(string relativePath);
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

    public Guid? StorageConfigOf(string relativePath)
        => relativePath.Split('/') is [_, var directory, ..]
            && directory.Split('-', 2) is [var config, _]
            && Guid.TryParseExact(config, "N", out var id)
                ? id
                : null;
}
