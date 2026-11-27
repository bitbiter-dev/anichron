using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record OwnedProxy(string ProxyPath, string ContentHash);

public interface IMediaAssetReadRepository
{
    /// <summary>The proxy of an asset the caller owns in an active config, active or soft-deleted.</summary>
    /// <param name="assetId">The asset.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="proxyType">The proxy's type.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>Null when the caller does not own the asset or it has no such proxy.</returns>
    Task<OwnedProxy?> FindOwnedProxyAsync(Guid assetId, Guid userId, ProxyType proxyType, CancellationToken ct);
}

public sealed class EfMediaAssetReadRepository(AnichronDbContext db) : IMediaAssetReadRepository
{
    public Task<OwnedProxy?> FindOwnedProxyAsync(Guid assetId, Guid userId, ProxyType proxyType, CancellationToken ct)
        => OwnedProxyQuery(db, assetId, userId, proxyType).FirstOrDefaultAsync(ct);

    internal static IQueryable<OwnedProxy> OwnedProxyQuery(
        AnichronDbContext db, Guid assetId, Guid userId, ProxyType proxyType)
        => db.MediaAssets.IgnoreQueryFilters().OwnedBy(userId)
            .Where(a => a.Id == assetId)
            .Join(
                db.ProxyFiles.IgnoreQueryFilters().Where(p => p.ProxyType == proxyType),
                a => a.Id,
                p => p.AssetId,
                (a, p) => new OwnedProxy(p.ProxyPath, a.ContentHash));
}
