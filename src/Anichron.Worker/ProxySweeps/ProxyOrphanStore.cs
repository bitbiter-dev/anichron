using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Worker.ProxySweeps;

internal sealed record FoundOrphan(string ProxyPath, Guid? StorageConfigId, long SizeBytes, Instant LastWriteTime);

internal interface IProxyOrphanStore
{
    /// <summary>Every <see cref="ProxyFile.ProxyPath"/> under a top-level shard folder, soft-deleted assets' included.</summary>
    /// <param name="shard">The shard folder's name.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<IReadOnlySet<string>> ReferencedUnderAsync(string shard, CancellationToken ct);

    /// <summary>Whether any <see cref="ProxyFile"/> row, a soft-deleted asset's included, references the path.</summary>
    /// <param name="proxyPath">A path relative to the proxy root.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<bool> IsReferencedAsync(string proxyPath, CancellationToken ct);

    /// <summary>Adds the orphans to the sweep's report, skipping paths it already holds.</summary>
    /// <param name="sweepId">The sweep's work item.</param>
    /// <param name="orphans">The orphans found.</param>
    /// <param name="ct">Cancels the insert.</param>
    Task RecordAsync(Guid sweepId, IReadOnlyList<FoundOrphan> orphans, CancellationToken ct);

    /// <summary>What the sweep's report holds, rows an earlier attempt of it recorded included.</summary>
    /// <param name="sweepId">The sweep's work item.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<ProxySweepResult> SummariseAsync(Guid sweepId, CancellationToken ct);

    /// <summary>
    /// The first unprocessed orphans in the deletion's scope, by id: of its config, or, unscoped, of no
    /// existing config.
    /// </summary>
    /// <param name="deletion">The sweep, the scope, and the optional selection.</param>
    /// <param name="take">How many to read at most.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<IReadOnlyList<ProxyOrphan>> GetPendingAsync(ProxyDeletionPayload deletion, int take, CancellationToken ct);

    /// <summary>Stops tracking the orphans read so far.</summary>
    void ForgetLoaded();
}

internal sealed class EfProxyOrphanStore(AnichronDbContext db, IGuidFactory guids) : IProxyOrphanStore
{
    public async Task<IReadOnlySet<string>> ReferencedUnderAsync(string shard, CancellationToken ct)
        => (await ReferencedUnderQuery(db, shard).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    public Task<bool> IsReferencedAsync(string proxyPath, CancellationToken ct)
        => db.ProxyFiles.IgnoreQueryFilters().AnyAsync(p => p.ProxyPath == proxyPath, ct);

    // Stryker disable all
    public async Task RecordAsync(Guid sweepId, IReadOnlyList<FoundOrphan> orphans, CancellationToken ct)
    {
        if (orphans.Count == 0)
            return;

        var ids = orphans.Select(_ => guids.NewGuid()).ToArray();
        var paths = orphans.Select(o => o.ProxyPath).ToArray();
        var configs = orphans.Select(o => o.StorageConfigId).ToArray();
        var sizes = orphans.Select(o => o.SizeBytes).ToArray();
        var times = orphans.Select(o => o.LastWriteTime).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "ProxyOrphans" ("Id", "SweepId", "ProxyPath", "StorageConfigId", "SizeBytes", "LastWriteTime")
            SELECT o.id, {sweepId}, o.path, o.config, o.size, o.time
            FROM unnest({ids}, {paths}, {configs}, {sizes}, {times}) AS o(id, path, config, size, time)
            ON CONFLICT ("SweepId", "ProxyPath") DO NOTHING
            """,
            ct);
    }

    public async Task<ProxySweepResult> SummariseAsync(Guid sweepId, CancellationToken ct)
    {
        var report = db.ProxyOrphans.Where(o => o.SweepId == sweepId);
        var live = ProxyOrphanScope.LiveConfigIds(db);
        return new ProxySweepResult(
            await report.CountAsync(ct),
            await report.SumAsync(o => o.SizeBytes, ct),
            await report.CountAsync(o => o.ProxyPath.EndsWith(ProxyStagingWriter.TemporaryFileSuffix), ct),
            await report
                .Where(o => o.StorageConfigId != null && !live.Contains(o.StorageConfigId))
                .Select(o => o.StorageConfigId)
                .Distinct()
                .CountAsync(ct));
    }

    public async Task<IReadOnlyList<ProxyOrphan>> GetPendingAsync(ProxyDeletionPayload deletion, int take, CancellationToken ct)
    {
        var pending = db.ProxyOrphans.Where(o => o.SweepId == deletion.SweepId && o.DeletionOutcome == null);
        var live = ProxyOrphanScope.LiveConfigIds(db);
        pending = deletion.StorageConfigId is { } configId
            ? pending.Where(o => o.StorageConfigId == configId)
            : pending.Where(o => o.StorageConfigId == null || !live.Contains(o.StorageConfigId));
        if (deletion.OrphanIds is { } selected)
            pending = pending.Where(o => selected.Contains(o.Id));
        return await pending.OrderBy(o => o.Id).Take(take).ToListAsync(ct);
    }

    public void ForgetLoaded() => db.ChangeTracker.Clear();

    internal static IQueryable<string> ReferencedUnderQuery(AnichronDbContext db, string shard)
        => db.ProxyFiles.IgnoreQueryFilters()
            .Where(p => p.ProxyPath.StartsWith(shard + "/"))
            .Select(p => p.ProxyPath);
}
