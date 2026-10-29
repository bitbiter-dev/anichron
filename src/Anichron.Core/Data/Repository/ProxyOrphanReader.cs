using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record ProxyOrphanView(
    Guid Id,
    string ProxyPath,
    Guid? StorageConfigId,
    long SizeBytes,
    Instant LastWriteTime,
    ProxyDeletionOutcome? DeletionOutcome,
    Instant? ProcessedAt);

/// <summary>Which orphans of a sweep to page through.</summary>
/// <param name="SweepId">The sweep.</param>
/// <param name="Outcome">Only orphans a deletion gave this outcome, or null for any.</param>
/// <param name="Pending">Only orphans no deletion processed yet.</param>
/// <param name="StorageConfigId">Only orphans of this storage config, or null for any.</param>
/// <param name="Skip">Orphans before the page.</param>
/// <param name="Take">The page size.</param>
public sealed record ProxyOrphanQuery(Guid SweepId, ProxyDeletionOutcome? Outcome, bool Pending, Guid? StorageConfigId, int Skip, int Take);

public sealed record ProxyOrphanPage(IReadOnlyList<ProxyOrphanView> Items, int Total);

/// <summary>An orphan's id and the live storage config its directory names.</summary>
/// <param name="Id">The orphan.</param>
/// <param name="LiveStorageConfigId">The config, or null when its path names none or the config no longer exists.</param>
public sealed record ProxyOrphanScope(Guid Id, Guid? LiveStorageConfigId)
{
    /// <summary>The ids of the storage configs that still exist, active or not: the configs an orphan's scope can be.</summary>
    /// <param name="db">The context to query.</param>
    public static IQueryable<Guid?> LiveConfigIds(AnichronDbContext db) => db.StorageConfigs.Select(c => (Guid?)c.Id);
}

public interface IProxyOrphanReader
{
    /// <summary>A page of a sweep's orphans, by path.</summary>
    /// <param name="query">The sweep, the optional outcome and config filters, and the page.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The page, and the number of orphans matching the filters.</returns>
    Task<ProxyOrphanPage> ListAsync(ProxyOrphanQuery query, CancellationToken ct);

    /// <summary>The orphans of a sweep among the ids; ids that are not orphans of it are left out.</summary>
    /// <param name="sweepId">The sweep.</param>
    /// <param name="ids">The orphans asked for.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<IReadOnlyList<ProxyOrphanScope>> FindAsync(Guid sweepId, IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Each live storage config, null for none, that a sweep's unprocessed orphans name.</summary>
    /// <param name="sweepId">The sweep.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<IReadOnlyList<Guid?>> PendingConfigsAsync(Guid sweepId, CancellationToken ct);
}

public sealed class EfProxyOrphanReader(AnichronDbContext db) : IProxyOrphanReader
{
    public async Task<ProxyOrphanPage> ListAsync(ProxyOrphanQuery query, CancellationToken ct)
    {
        var matching = db.ProxyOrphans.Where(o => o.SweepId == query.SweepId);
        if (query.Outcome is { } outcome)
            matching = matching.Where(o => o.DeletionOutcome == outcome);
        if (query.Pending)
            matching = matching.Where(o => o.DeletionOutcome == null);
        if (query.StorageConfigId is { } configId)
            matching = matching.Where(o => o.StorageConfigId == configId);

        var items = await matching
            .OrderBy(o => o.ProxyPath)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(o => new ProxyOrphanView(
                o.Id, o.ProxyPath, o.StorageConfigId, o.SizeBytes, o.LastWriteTime, o.DeletionOutcome, o.ProcessedAt))
            .ToListAsync(ct);
        return new ProxyOrphanPage(items, await matching.CountAsync(ct));
    }

    public async Task<IReadOnlyList<ProxyOrphanScope>> FindAsync(Guid sweepId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var live = ProxyOrphanScope.LiveConfigIds(db);
        return await db.ProxyOrphans
            .Where(o => o.SweepId == sweepId && ids.Contains(o.Id))
            .Select(o => new ProxyOrphanScope(o.Id, live.Contains(o.StorageConfigId) ? o.StorageConfigId : null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid?>> PendingConfigsAsync(Guid sweepId, CancellationToken ct)
    {
        var live = ProxyOrphanScope.LiveConfigIds(db);
        return await db.ProxyOrphans
            .Where(o => o.SweepId == sweepId && o.DeletionOutcome == null)
            .Select(o => live.Contains(o.StorageConfigId) ? o.StorageConfigId : null)
            .Distinct()
            .ToListAsync(ct);
    }
}
