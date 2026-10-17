using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record VisibleProxy(string ProxyPath, string ContentHash);

public interface IMediaAssetReadRepository
{
    Task<VisibleProxy?> FindVisibleProxyAsync(Guid assetId, Guid userId, ProxyType proxyType, CancellationToken ct);
}

public sealed class EfMediaAssetReadRepository(AnichronDbContext db) : IMediaAssetReadRepository
{
    public Task<VisibleProxy?> FindVisibleProxyAsync(Guid assetId, Guid userId, ProxyType proxyType, CancellationToken ct)
        => VisibleProxyQuery(db, assetId, userId, proxyType).FirstOrDefaultAsync(ct);

    internal static IQueryable<VisibleProxy> VisibleProxyQuery(
        AnichronDbContext db, Guid assetId, Guid userId, ProxyType proxyType)
        => db.MediaAssets.VisibleTo(userId)
            .Where(a => a.Id == assetId)
            .Join(
                db.ProxyFiles.Where(p => p.ProxyType == proxyType),
                a => a.Id,
                p => p.AssetId,
                (a, p) => new VisibleProxy(p.ProxyPath, a.ContentHash));
}
