using Anichron.Core.Domain;
using Anichron.Core.Work;
using NodaTime;

namespace Anichron.Worker.Work;

public sealed record WorkAttempt(Guid Id, int Attempt);

public sealed record ClaimedWork(WorkAttempt Attempt, WorkKind Kind, string Payload);

/// <summary>
/// Every state change a Worker makes to a work item after it was enqueued. Writes about a running
/// item are fenced by its <see cref="WorkAttempt"/>: once the item was recovered and claimed again,
/// the older attempt changes nothing.
/// </summary>
public interface IWorkItemStore
{
    /// <summary>The oldest queued item, now running under a lease, or null when none is queued.</summary>
    /// <param name="claimedBy">The claiming Worker's display name.</param>
    /// <param name="now">The claim time.</param>
    /// <param name="lease">How long the claim holds without a heartbeat.</param>
    /// <param name="ct">Cancels the claim.</param>
    /// <returns>The claimed item, or null.</returns>
    Task<ClaimedWork?> ClaimAsync(string claimedBy, Instant now, Duration lease, CancellationToken ct);

    /// <summary>Extends the lease and stores the progress snapshot.</summary>
    /// <param name="attempt">The running attempt.</param>
    /// <param name="progress">The latest snapshot.</param>
    /// <param name="now">The heartbeat time.</param>
    /// <param name="lease">How long the lease holds from now.</param>
    /// <param name="ct">Cancels the heartbeat.</param>
    /// <returns>False when the attempt no longer holds the item.</returns>
    Task<bool> HeartbeatAsync(WorkAttempt attempt, WorkProgress progress, Instant now, Duration lease, CancellationToken ct);

    Task SucceedAsync(WorkAttempt attempt, WorkProgress progress, string? result, Instant now, CancellationToken ct);

    Task FailAsync(WorkAttempt attempt, WorkProgress progress, string error, Instant now, CancellationToken ct);

    /// <summary>Hands the item back to the queue without counting a stall.</summary>
    /// <param name="attempt">The running attempt.</param>
    /// <param name="now">The release time.</param>
    /// <param name="ct">Cancels the release.</param>
    /// <returns>A task that completes once the item is queued again.</returns>
    Task ReleaseAsync(WorkAttempt attempt, Instant now, CancellationToken ct);

    /// <summary>
    /// Puts running items whose lease expired back in the queue, counting a stall, and fails those
    /// reaching <paramref name="maxStalls"/> as stalled.
    /// </summary>
    /// <param name="now">Leases ending before this have expired.</param>
    /// <param name="maxStalls">The stall that fails an item.</param>
    /// <param name="ct">Cancels the recovery.</param>
    /// <returns>A task that completes once every expired lease is recovered.</returns>
    Task RecoverExpiredLeasesAsync(Instant now, int maxStalls, CancellationToken ct);

    /// <summary>
    /// True when no item with <paramref name="dedupKey"/> is queued or running and none finished
    /// after <paramref name="finishedBefore"/>.
    /// </summary>
    /// <param name="dedupKey">The periodic kind's key.</param>
    /// <param name="finishedBefore">Now minus the kind's interval.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>Whether the kind is due.</returns>
    Task<bool> IsDueAsync(string dedupKey, Instant finishedBefore, CancellationToken ct);

    /// <summary>Deletes finished items that finished before <paramref name="cutoff"/>.</summary>
    /// <param name="cutoff">Items finished before this go.</param>
    /// <param name="ct">Cancels the delete.</param>
    /// <returns>How many items were deleted.</returns>
    Task<int> DeleteFinishedBeforeAsync(Instant cutoff, CancellationToken ct);
}
