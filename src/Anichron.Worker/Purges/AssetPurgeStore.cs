using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Purges;

/// <summary>A purged asset and one of its proxy paths.</summary>
/// <param name="AssetId">The purged asset.</param>
/// <param name="ProxyPath">One of its proxy paths, relative to the proxy root, or null when it had none.</param>
internal sealed record PurgedProxy(Guid AssetId, string? ProxyPath);

internal interface IAssetPurgeStore
{
    /// <summary>
    /// Deletes the config's rows among the ids that are still soft-deleted, with everything that cascades from them.
    /// </summary>
    /// <param name="storageConfigId">The config the rows must belong to.</param>
    /// <param name="assetIds">The rows to delete.</param>
    /// <param name="ct">Cancels the delete.</param>
    /// <returns>One entry per proxy path of each deleted row, or one without a path for a row that had none.</returns>
    Task<IReadOnlyList<PurgedProxy>> DeleteSoftDeletedAsync(Guid storageConfigId, IReadOnlyList<Guid> assetIds, CancellationToken ct);

    /// <summary>How many of the ids are rows of the config, soft-deleted or not.</summary>
    /// <param name="storageConfigId">The config.</param>
    /// <param name="assetIds">The ids to look for.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<int> CountPresentAsync(Guid storageConfigId, IReadOnlyList<Guid> assetIds, CancellationToken ct);

    /// <summary>Whether any <see cref="ProxyFile"/> row, a soft-deleted asset's included, references the path.</summary>
    /// <param name="proxyPath">A path relative to the proxy root.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<bool> IsReferencedAsync(string proxyPath, CancellationToken ct);
}

internal sealed class EfAssetPurgeStore(AnichronDbContext db) : IAssetPurgeStore
{
    // Stryker disable all
    public async Task<IReadOnlyList<PurgedProxy>> DeleteSoftDeletedAsync(
        Guid storageConfigId, IReadOnlyList<Guid> assetIds, CancellationToken ct)
    {
        var ids = assetIds.ToArray();
        return await db.Database.SqlQuery<PurgedProxy>(
            $"""
            WITH gone AS (
                DELETE FROM "MediaAssets"
                WHERE "Id" = ANY({ids}) AND "StorageConfigId" = {storageConfigId} AND "IsSoftDeleted"
                RETURNING "Id"
            )
            SELECT gone."Id" AS "AssetId", p."ProxyPath" AS "ProxyPath"
            FROM gone LEFT JOIN "ProxyFiles" p ON p."AssetId" = gone."Id"
            """)
            .ToListAsync(ct);
    }

    public Task<int> CountPresentAsync(Guid storageConfigId, IReadOnlyList<Guid> assetIds, CancellationToken ct)
        => db.MediaAssets.IgnoreQueryFilters().CountAsync(a => a.StorageConfigId == storageConfigId && assetIds.Contains(a.Id), ct);

    public Task<bool> IsReferencedAsync(string proxyPath, CancellationToken ct)
        => db.ProxyFiles.IgnoreQueryFilters().AnyAsync(p => p.ProxyPath == proxyPath, ct);
}
