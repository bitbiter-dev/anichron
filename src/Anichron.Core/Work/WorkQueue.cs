using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Work;

public interface IWorkQueue
{
    /// <summary>
    /// Adds a queued item, or returns the id of the queued item that already holds the payload's
    /// dedup key. Saves at once, so call it on a context with no other pending changes.
    /// </summary>
    /// <typeparam name="TPayload">The payload type, which names the kind.</typeparam>
    /// <param name="payload">What the handler receives.</param>
    /// <param name="requestedBy">The requesting admin, or null when scheduled.</param>
    /// <param name="ct">Cancels the enqueue.</param>
    /// <returns>The id of the queued item.</returns>
    Task<Guid> EnqueueAsync<TPayload>(TPayload payload, Guid? requestedBy, CancellationToken ct)
        where TPayload : IWorkPayload;
}

public sealed class EfWorkQueue(AnichronDbContext db, IClock clock, IGuidFactory guids) : IWorkQueue
{
    internal const int MaxInsertAttempts = 3;

    public async Task<Guid> EnqueueAsync<TPayload>(TPayload payload, Guid? requestedBy, CancellationToken ct)
        where TPayload : IWorkPayload
    {
        var dedupKey = payload.DedupKey;
        for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
        {
            if (dedupKey is not null)
            {
                var waiting = await db.WorkItems
                    .Where(w => w.DedupKey == dedupKey && w.Status == WorkItemStatus.Queued)
                    .Select(w => (Guid?)w.Id)
                    .FirstOrDefaultAsync(ct);
                if (waiting is { } id)
                    return id;
            }

            var now = clock.GetCurrentInstant();
            var item = new WorkItem
            {
                Id = guids.NewGuid(),
                Kind = TPayload.Kind,
                Payload = WorkJson.Serialize(payload),
                DedupKey = dedupKey,
                StorageConfigId = payload.StorageConfigId,
                RequestedBy = requestedBy,
                RequestedAt = now,
                Status = WorkItemStatus.Queued,
                UpdatedAt = now,
            };
            if (await TryInsertAsync(item, ct))
                return item.Id;
        }

        throw new InvalidOperationException(
            $"Enqueueing work with dedup key '{dedupKey}' lost the insert race {MaxInsertAttempts} times.");
    }

    // Stryker disable all
    private async Task<bool> TryInsertAsync(WorkItem item, CancellationToken ct)
    {
        db.WorkItems.Add(item);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(WorkItemIndexNames.QueuedDedupKeyUnique))
        {
            db.Entry(item).State = EntityState.Detached;
            return false;
        }
    }
}
