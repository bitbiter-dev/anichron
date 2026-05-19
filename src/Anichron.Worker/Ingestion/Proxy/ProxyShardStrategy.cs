namespace Anichron.Worker.Ingestion.Proxy;

internal interface IProxyDirectoryStrategy
{
    string GetDirectory(Guid assetId);
}

internal sealed class TwoLevelHexShardStrategy : IProxyDirectoryStrategy
{
    // Produces {first 2 hex chars}/{remaining 30 hex chars} — caps the top-level bucket count at 256.
    public string GetDirectory(Guid assetId)
    {
        var hex = assetId.ToString("N");
        return $"{hex[..2]}/{hex[2..]}";
    }
}
