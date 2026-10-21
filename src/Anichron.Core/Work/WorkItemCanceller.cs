using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Work;

public enum CancelOutcome
{
    /// <summary>The item was queued and is now cancelled; it never starts.</summary>
    Cancelled = 1,

    /// <summary>The item is running; its Worker cancels the handler at the next heartbeat.</summary>
    Requested = 2,

    Finished = 3,
    NotFound = 4,
}

public interface IWorkItemCanceller
{
    Task<CancelOutcome> CancelAsync(Guid id, CancellationToken ct);
}

public sealed class EfWorkItemCanceller(AnichronDbContext db, IClock clock) : IWorkItemCanceller
{
    // Stryker disable all
    public async Task<CancelOutcome> CancelAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var statuses = await db.Database.SqlQuery<string>($"""
            UPDATE "WorkItems"
            SET "Status" = CASE WHEN "Status" = 'Queued' THEN 'Cancelled' ELSE "Status" END,
                "FinishedAt" = CASE WHEN "Status" = 'Queued' THEN {now} ELSE "FinishedAt" END,
                "CancelRequestedAt" = COALESCE("CancelRequestedAt", {now}),
                "UpdatedAt" = CASE WHEN "CancelRequestedAt" IS NULL THEN {now} ELSE "UpdatedAt" END
            WHERE "Id" = {id} AND "Status" IN ('Queued', 'Running')
            RETURNING "Status" AS "Value"
            """).ToListAsync(ct);
        return statuses switch
        {
            [nameof(WorkItemStatus.Cancelled)] => CancelOutcome.Cancelled,
            [_] => CancelOutcome.Requested,
            _ => await db.WorkItems.AnyAsync(w => w.Id == id, ct) ? CancelOutcome.Finished : CancelOutcome.NotFound,
        };
    }
}
