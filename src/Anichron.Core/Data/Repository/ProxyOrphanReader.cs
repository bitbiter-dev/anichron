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

public sealed record ProxyOrphanQuery(Guid SweepId, ProxyDeletionOutcome? Outcome, Guid? StorageConfigId, int Skip, int Take);

public sealed record ProxyOrphanPage(IReadOnlyList<ProxyOrphanView> Items, int Total);

/// <summary>An orphan's id and the storage config its directory names.</summary>
/// <param name="Id">The orphan.</param>
/// <param name="StorageConfigId">The config, or null when its path names none.</param>
public sealed record ProxyOrphanScope(Guid Id, Guid? StorageConfigId);

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

    /// <summary>Each storage config, null included, that a sweep's unprocessed orphans name.</summary>
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
        => await db.ProxyOrphans
            .Where(o => o.SweepId == sweepId && ids.Contains(o.Id))
            .Select(o => new ProxyOrphanScope(o.Id, o.StorageConfigId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid?>> PendingConfigsAsync(Guid sweepId, CancellationToken ct)
        => await db.ProxyOrphans
            .Where(o => o.SweepId == sweepId && o.DeletionOutcome == null)
            .Select(o => o.StorageConfigId)
            .Distinct()
            .ToListAsync(ct);
}
