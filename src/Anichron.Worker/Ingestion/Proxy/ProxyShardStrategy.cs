namespace Anichron.Worker.Ingestion.Proxy;

internal interface IProxyDirectoryStrategy
{
    string GetDirectory(Guid assetId);
}

internal sealed class TwoLevelHexShardStrategy : IProxyDirectoryStrategy
{
    // Two hex chars → 256 top-level buckets; prevents filesystem directory-count blowup at large library sizes.
    public string GetDirectory(Guid assetId)
    {
        var hex = assetId.ToString("N");
        return $"{hex[..2]}/{hex[2..]}";
    }
}
