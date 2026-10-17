using Anichron.Core;
using Anichron.Core.Domain;
using NodaTime;

namespace Anichron.Worker.Ingestion.Proxy;

internal static class ProxyFileBuilder
{
    internal static ProxyFile Build(
        Guid assetId,
        string relativePath,
        ProxyType proxyType,
        long sizeBytes,
        IGuidFactory guidFactory,
        IClock clock)
        => new()
        {
            Id = guidFactory.NewGuid(),
            AssetId = assetId,
            ProxyPath = relativePath,
            ProxyType = proxyType,
            SizeBytes = sizeBytes,
            CreatedAt = clock.GetCurrentInstant(),
        };
}
