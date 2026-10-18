using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Worker.Work;

public sealed class EfWorkItemStore(IDbContextFactory<AnichronDbContext> contexts) : IWorkItemStore
{
    internal const string StalledError = "Stalled: the lease expired without a heartbeat too many times.";

    private sealed record ClaimRow(Guid Id, string Kind, string Payload, int Attempt);

    // Stryker disable all
    public async Task<ClaimedWork?> ClaimAsync(string claimedBy, Instant now, Duration lease, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var leaseUntil = now + lease;
        var rows = await db.Database.SqlQuery<ClaimRow>($"""
            UPDATE "WorkItems"
            SET "Status" = 'Running', "Attempt" = "Attempt" + 1, "ClaimedBy" = {claimedBy},
                "StartedAt" = {now}, "LeaseUntil" = {leaseUntil}, "UpdatedAt" = {now},
                "ProgressDone" = 0, "ProgressTotal" = NULL, "ProgressNote" = NULL
            WHERE "Id" = (
                SELECT "Id" FROM "WorkItems"
                WHERE "Status" = 'Queued'
                ORDER BY "RequestedAt", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id", "Kind", "Payload", "Attempt"
            """).ToListAsync(ct);
        return rows is [var row]
            ? new ClaimedWork(new WorkAttempt(row.Id, row.Attempt), Enum.Parse<WorkKind>(row.Kind), row.Payload)
            : null;
    }

    // Stryker disable all
    public async Task<bool> HeartbeatAsync(
        WorkAttempt attempt, WorkProgress progress, Instant now, Duration lease, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var rows = await Running(db, attempt).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.LeaseUntil, now + lease)
            .SetProperty(w => w.ProgressDone, progress.Done)
            .SetProperty(w => w.ProgressTotal, progress.Total)
            .SetProperty(w => w.ProgressNote, progress.Note)
            .SetProperty(w => w.UpdatedAt, now), ct);
        return rows == 1;
    }

    // Stryker disable all
    public async Task SucceedAsync(
        WorkAttempt attempt, WorkProgress progress, string? result, Instant now, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await Running(db, attempt).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Succeeded)
            .SetProperty(w => w.ProgressDone, progress.Done)
            .SetProperty(w => w.ProgressTotal, progress.Total)
            .SetProperty(w => w.ProgressNote, progress.Note)
            .SetProperty(w => w.Result, result)
            .SetProperty(w => w.FinishedAt, now)
            .SetProperty(w => w.UpdatedAt, now), ct);
    }

    // Stryker disable all
    public async Task FailAsync(
        WorkAttempt attempt, WorkProgress progress, string error, Instant now, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await Running(db, attempt).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Failed)
            .SetProperty(w => w.ProgressDone, progress.Done)
            .SetProperty(w => w.ProgressTotal, progress.Total)
            .SetProperty(w => w.ProgressNote, progress.Note)
            .SetProperty(w => w.Error, error)
            .SetProperty(w => w.FinishedAt, now)
            .SetProperty(w => w.UpdatedAt, now), ct);
    }

    // Stryker disable all
    public async Task ReleaseAsync(WorkAttempt attempt, Instant now, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await Running(db, attempt).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Queued)
            .SetProperty(w => w.ClaimedBy, (string?)null)
            .SetProperty(w => w.StartedAt, (Instant?)null)
            .SetProperty(w => w.LeaseUntil, (Instant?)null)
            .SetProperty(w => w.UpdatedAt, now), ct);
    }

    // Stryker disable all
    public async Task RecoverExpiredLeasesAsync(Instant now, int maxStalls, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await Expired(db, now).Where(w => w.Stalls + 1 >= maxStalls).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Failed)
            .SetProperty(w => w.Stalls, w => w.Stalls + 1)
            .SetProperty(w => w.Error, StalledError)
            .SetProperty(w => w.FinishedAt, now)
            .SetProperty(w => w.UpdatedAt, now), ct);
        await Expired(db, now).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Queued)
            .SetProperty(w => w.Stalls, w => w.Stalls + 1)
            .SetProperty(w => w.ClaimedBy, (string?)null)
            .SetProperty(w => w.StartedAt, (Instant?)null)
            .SetProperty(w => w.LeaseUntil, (Instant?)null)
            .SetProperty(w => w.UpdatedAt, now), ct);
    }

    public async Task<bool> IsDueAsync(string dedupKey, Instant finishedBefore, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return !await db.WorkItems.AnyAsync(w => w.DedupKey == dedupKey
            && (w.Status == WorkItemStatus.Queued
                || w.Status == WorkItemStatus.Running
                || w.FinishedAt > finishedBefore), ct);
    }

    // Stryker disable all
    public async Task<int> DeleteFinishedBeforeAsync(Instant cutoff, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.WorkItems.Where(w => w.FinishedAt < cutoff).ExecuteDeleteAsync(ct);
    }

    // Stryker disable all
    private static IQueryable<WorkItem> Running(AnichronDbContext db, WorkAttempt attempt)
        => db.WorkItems.Where(w =>
            w.Id == attempt.Id && w.Attempt == attempt.Attempt && w.Status == WorkItemStatus.Running);

    // Stryker disable all
    private static IQueryable<WorkItem> Expired(AnichronDbContext db, Instant now)
        => db.WorkItems.Where(w => w.Status == WorkItemStatus.Running && w.LeaseUntil < now);
}
