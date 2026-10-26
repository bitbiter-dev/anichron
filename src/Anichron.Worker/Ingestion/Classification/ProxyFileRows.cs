using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion.Classification;

internal static class ProxyFileRows
{
    /// <summary>
    /// Makes the asset's proxy rows describe <paramref name="replacements"/>, by type: an existing row of a
    /// type is updated in place, a missing type is added, and a type not among the replacements is removed.
    /// </summary>
    /// <param name="repository">Tracks the added and removed rows.</param>
    /// <param name="asset">The asset whose proxy rows change.</param>
    /// <param name="replacements">The proxies the asset has from now on, owned by the asset.</param>
    public static void Replace(IMediaAssetRepository repository, MediaAsset asset, IReadOnlyList<ProxyFile> replacements)
    {
        var stale = asset.ProxyFiles.Where(old => replacements.All(proxy => proxy.ProxyType != old.ProxyType));
        foreach (var proxy in stale.ToList())
            repository.RemoveProxyFile(proxy);

        var added = new List<ProxyFile>();
        foreach (var proxy in replacements)
        {
            if (asset.ProxyFiles.FirstOrDefault(old => old.ProxyType == proxy.ProxyType) is not { } existing)
            {
                added.Add(proxy);
                continue;
            }

            existing.ProxyPath = proxy.ProxyPath;
            existing.SizeBytes = proxy.SizeBytes;
            existing.CreatedAt = proxy.CreatedAt;
        }

        repository.AddProxyFiles(added);
    }
}
