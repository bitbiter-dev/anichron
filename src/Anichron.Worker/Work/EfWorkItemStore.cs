using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace Anichron.Worker.Work;

public sealed class EfWorkItemStore(IDbContextFactory<AnichronDbContext> contexts) : IWorkItemStore
{
    internal const string StalledError = "Stalled: the lease expired without a heartbeat too many times.";
    internal const string SupersededError = "Superseded by the queued item with the same dedup key.";

    private sealed record ClaimRow(Guid Id, string Kind, string Payload, int Attempt);

    // Stryker disable all
    public async Task<ClaimedWork?> ClaimAsync(
        string claimedBy, Guid? servedUserId, Instant now, Duration lease, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var leaseUntil = now + lease;
        var kinds = Enum.GetNames<WorkKind>();
        var rows = await db.Database.SqlQuery<ClaimRow>($"""
            UPDATE "WorkItems"
            SET "Status" = 'Running', "Attempt" = "Attempt" + 1, "ClaimedBy" = {claimedBy},
                "StartedAt" = {now}, "LeaseUntil" = {leaseUntil}, "UpdatedAt" = {now},
                "ProgressDone" = 0, "ProgressTotal" = NULL, "ProgressNote" = NULL
            WHERE "Id" = (
                SELECT "Id" FROM "WorkItems"
                WHERE "Status" = 'Queued' AND "Kind" = ANY({kinds})
                  AND ("StorageConfigId" IS NULL
                    OR CAST({servedUserId} AS uuid) IS NULL
                    OR "StorageConfigId" IN (
                        SELECT "Id" FROM "StorageConfigs" WHERE "UserId" = CAST({servedUserId} AS uuid)))
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
    public async Task<HeartbeatOutcome> HeartbeatAsync(
        WorkAttempt attempt, WorkProgress progress, Instant now, Duration lease, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var rows = await Running(db, attempt).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.LeaseUntil, now + lease)
            .SetProperty(w => w.ProgressDone, progress.Done)
            .SetProperty(w => w.ProgressTotal, progress.Total)
            .SetProperty(w => w.ProgressNote, progress.Note)
            .SetProperty(
                w => w.UpdatedAt,
                w => w.ProgressDone == progress.Done && w.ProgressTotal == progress.Total && w.ProgressNote == progress.Note
                    ? w.UpdatedAt
                    : now), ct);
        if (rows == 0)
            return HeartbeatOutcome.Lost;
        return await Running(db, attempt).AnyAsync(w => w.CancelRequestedAt != null, ct)
            ? HeartbeatOutcome.CancelRequested
            : HeartbeatOutcome.Held;
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
    public async Task CancelAsync(
        WorkAttempt attempt, WorkProgress progress, string? result, Instant now, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await Running(db, attempt).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Cancelled)
            .SetProperty(w => w.ProgressDone, progress.Done)
            .SetProperty(w => w.ProgressTotal, progress.Total)
            .SetProperty(w => w.ProgressNote, progress.Note)
            .SetProperty(w => w.Result, result)
            .SetProperty(w => w.FinishedAt, now)
            .SetProperty(w => w.UpdatedAt, now), ct);
    }

    // Stryker disable all
    public async Task ReleaseAsync(WorkAttempt attempt, Instant now, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await ReturnToQueueAsync(db, attempt, expiredBefore: null, stall: 0, now, ct);
    }

    // Stryker disable all
    public async Task RecoverExpiredLeasesAsync(Instant now, int maxStalls, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await Expired(db, now).Where(w => w.Stalls + 1 >= maxStalls && w.CancelRequestedAt == null).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WorkItemStatus.Failed)
            .SetProperty(w => w.Stalls, w => w.Stalls + 1)
            .SetProperty(w => w.Error, StalledError)
            .SetProperty(w => w.FinishedAt, now)
            .SetProperty(w => w.UpdatedAt, now), ct);
        var expired = await Expired(db, now).Select(w => new WorkAttempt(w.Id, w.Attempt)).ToListAsync(ct);
        foreach (var attempt in expired)
            await ReturnToQueueAsync(db, attempt, expiredBefore: now, stall: 1, now, ct);
    }

    // Stryker disable all
    private static async Task ReturnToQueueAsync(
        AnichronDbContext db, WorkAttempt attempt, Instant? expiredBefore, int stall, Instant now, CancellationToken ct)
    {
        var running = Running(db, attempt);
        if (expiredBefore is { } before)
            running = running.Where(w => w.LeaseUntil < before);

        for (var tries = 0; tries < 3; tries++)
        {
            if (await TryReturnToQueueOnceAsync(running, db, stall, now, ct))
                return;
        }

        throw new InvalidOperationException(
            $"Work item {attempt.Id} kept colliding with a queued item while returning to the queue.");
    }

    // Stryker disable all
    private static async Task<bool> TryReturnToQueueOnceAsync(
        IQueryable<WorkItem> running, AnichronDbContext db, int stall, Instant now, CancellationToken ct)
    {
        try
        {
            var requeued = await running
                .Where(w => w.CancelRequestedAt == null && !db.WorkItems.Any(q =>
                    q.Status == WorkItemStatus.Queued && q.DedupKey != null && q.DedupKey == w.DedupKey))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WorkItemStatus.Queued)
                    .SetProperty(w => w.Stalls, w => w.Stalls + stall)
                    .SetProperty(w => w.ClaimedBy, (string?)null)
                    .SetProperty(w => w.StartedAt, (Instant?)null)
                    .SetProperty(w => w.LeaseUntil, (Instant?)null)
                    .SetProperty(w => w.UpdatedAt, now), ct);
            if (requeued == 0)
            {
                await running.ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.Status, WorkItemStatus.Cancelled)
                    .SetProperty(w => w.Stalls, w => w.Stalls + stall)
                    .SetProperty(w => w.Error, w => w.CancelRequestedAt == null ? SupersededError : null)
                    .SetProperty(w => w.FinishedAt, now)
                    .SetProperty(w => w.UpdatedAt, now), ct);
            }

            return true;
        }
        catch (Exception ex) when (IsQueuedDedupViolation(ex))
        {
            return false;
        }
    }

    // Stryker disable all
    private static bool IsQueuedDedupViolation(Exception ex)
        => (ex as PostgresException ?? ex.InnerException as PostgresException) is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: WorkItemIndexNames.QueuedDedupKeyUnique,
        };

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
