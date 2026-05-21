using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;

namespace Anichron.Worker.Ingestion.Proxy;

internal static class ProxyFileBuilder
{
    internal static ProxyFile Build(
        IngestionContext context,
        string relativePath,
        ProxyType proxyType,
        long sizeBytes,
        IGuidFactory guidFactory,
        IClock clock)
        => new()
        {
            Id = guidFactory.NewGuid(),
            AssetId = context.AssetId,
            ProxyPath = relativePath,
            ProxyType = proxyType,
            SizeBytes = sizeBytes,
            CreatedAt = clock.GetCurrentInstant(),
        };
}
