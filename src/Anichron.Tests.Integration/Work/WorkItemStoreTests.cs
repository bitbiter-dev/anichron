using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Anichron.Tests.Integration.Work;

public sealed class WorkItemStoreTests(PostgresServer server)
{
    private static readonly Duration Lease = Duration.FromSeconds(60);
    private static readonly WorkProgress Halfway = new(5, 10, "halfway");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Instant At(int seconds) => WorkDatabase.Start + Duration.FromSeconds(seconds);

    [Fact]
    public async Task ClaimAsync_ConcurrentClaimers_NeverGetTheSameItemAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        for (var i = 0; i < 20; i++)
            await work.EnqueueAsync(new UnkeyedPayload(i), Ct);
        var claimed = new ConcurrentBag<Guid>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            while (await work.Store.ClaimAsync("worker", null, At(1), Lease, Ct) is { } item)
                claimed.Add(item.Attempt.Id);
        }, Ct)));

        claimed.Should().HaveCount(20).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ClaimAsync_OldestItemLockedByAnotherClaim_TakesTheNextWithoutWaitingAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var locked = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        work.Clock.Now = At(1);
        var next = await work.EnqueueAsync(new UnkeyedPayload(2), Ct);
        await using var other = work.CreateDbContext();
        await using var transaction = await other.Database.BeginTransactionAsync(Ct);
        await other.Database.ExecuteSqlAsync($"""SELECT 1 FROM "WorkItems" WHERE "Id" = {locked} FOR UPDATE""", Ct);

        var claimed = await work.Store.ClaimAsync("worker", null, At(10), Lease, Ct);

        claimed!.Attempt.Id.Should().Be(next);
    }

    [Fact]
    public async Task ClaimAsync_TakesTheOldestQueuedItemAndLeasesItAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var oldest = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        work.Clock.Now = At(1);
        await work.EnqueueAsync(new UnkeyedPayload(2), Ct);

        var claimed = await work.Store.ClaimAsync("all@host", null, At(10), Lease, Ct);

        claimed.Should().Be(new ClaimedWork(new WorkAttempt(oldest, 1), WorkKind.TokenCleanup, /*lang=json,strict*/ """{"number": 1}"""));
        var item = await work.ItemAsync(oldest, Ct);
        item.Status.Should().Be(WorkItemStatus.Running);
        item.ClaimedBy.Should().Be("all@host");
        item.StartedAt.Should().Be(At(10));
        item.LeaseUntil.Should().Be(At(70));
        item.UpdatedAt.Should().Be(At(10));
    }

    [Fact]
    public async Task ClaimAsync_NothingQueued_ReturnsNullAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);

        (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task HeartbeatAsync_HoldingAttempt_ExtendsTheLeaseAndStoresTheProgressAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;

        var held = await work.Store.HeartbeatAsync(claimed.Attempt, Halfway, At(30), Lease, Ct);

        held.Should().Be(HeartbeatOutcome.Held);
        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        item.LeaseUntil.Should().Be(At(90));
        (item.ProgressDone, item.ProgressTotal, item.ProgressNote).Should().Be((5L, 10L, "halfway"));
        item.UpdatedAt.Should().Be(At(30));
    }

    [Fact]
    public async Task HeartbeatAsync_SameProgress_ExtendsTheLeaseWithoutMovingUpdatedAtAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        await work.Store.HeartbeatAsync(claimed.Attempt, Halfway, At(30), Lease, Ct);

        var held = await work.Store.HeartbeatAsync(claimed.Attempt, new WorkProgress(5, 10, "halfway"), At(32), Lease, Ct);

        held.Should().Be(HeartbeatOutcome.Held);
        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        item.LeaseUntil.Should().Be(At(92));
        item.UpdatedAt.Should().Be(At(30));
    }

    [Fact]
    public async Task HeartbeatAsync_NoProgressSinceTheClaim_LeavesUpdatedAtAtTheClaimAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;

        await work.Store.HeartbeatAsync(claimed.Attempt, WorkProgress.None, At(2), Lease, Ct);

        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        item.LeaseUntil.Should().Be(At(62));
        item.UpdatedAt.Should().Be(At(0));
    }

    [Theory]
    [InlineData(6L, 10L, "halfway")]
    [InlineData(5L, null, "halfway")]
    [InlineData(5L, 11L, "halfway")]
    [InlineData(5L, 10L, null)]
    [InlineData(5L, 10L, "later")]
    public async Task HeartbeatAsync_AnyPartOfTheProgressChanged_MovesUpdatedAtAsync(long done, long? total, string? note)
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        await work.Store.HeartbeatAsync(claimed.Attempt, Halfway, At(30), Lease, Ct);

        await work.Store.HeartbeatAsync(claimed.Attempt, new WorkProgress(done, total, note), At(32), Lease, Ct);

        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        (item.ProgressDone, item.ProgressTotal, item.ProgressNote).Should().Be((done, total, note));
        item.UpdatedAt.Should().Be(At(32));
    }

    [Fact]
    public async Task AfterAReclaim_TheOldAttemptsHeartbeatAndCompletionChangeNothingAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var zombie = (await work.Store.ClaimAsync("paused", null, At(0), Lease, Ct))!;
        await work.Store.RecoverExpiredLeasesAsync(At(61), 3, Ct);
        var current = (await work.Store.ClaimAsync("alive", null, At(62), Lease, Ct))!;

        var held = await work.Store.HeartbeatAsync(zombie.Attempt, Halfway, At(63), Lease, Ct);
        await work.Store.SucceedAsync(zombie.Attempt, Halfway, /*lang=json,strict*/ """{"done": true}""", At(64), Ct);
        await work.Store.FailAsync(zombie.Attempt, Halfway, "boom", At(64), Ct);
        await work.Store.ReleaseAsync(zombie.Attempt, At(64), Ct);
        await work.Store.CancelAsync(zombie.Attempt, Halfway, /*lang=json,strict*/ """{"done": false}""", At(64), Ct);

        held.Should().Be(HeartbeatOutcome.Lost);
        current.Attempt.Should().Be(new WorkAttempt(zombie.Attempt.Id, 2));
        var item = await work.ItemAsync(current.Attempt.Id, Ct);
        item.Status.Should().Be(WorkItemStatus.Running);
        item.ClaimedBy.Should().Be("alive");
        (item.Result, item.Error, item.ProgressNote).Should().Be((null, null, null));
        item.UpdatedAt.Should().Be(At(62));
    }

    [Fact]
    public async Task RecoverExpiredLeasesAsync_RequeuesTwiceThenFailsTheThirdStallAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var stalls = new List<(WorkItemStatus, int)>();

        for (var round = 0; round < 3; round++)
        {
            var claimedAt = At(round * 100);
            await work.Store.ClaimAsync("worker", null, claimedAt, Lease, Ct);
            await work.Store.RecoverExpiredLeasesAsync(claimedAt + Duration.FromSeconds(61), 3, Ct);
            var item = await work.ItemAsync(id, Ct);
            stalls.Add((item.Status, item.Stalls));
        }

        stalls.Should().Equal((WorkItemStatus.Queued, 1), (WorkItemStatus.Queued, 2), (WorkItemStatus.Failed, 3));
        var failed = await work.ItemAsync(id, Ct);
        failed.Error.Should().Be(EfWorkItemStore.StalledError);
        failed.FinishedAt.Should().Be(At(261));
    }

    [Fact]
    public async Task RecoverExpiredLeasesAsync_LeaseStillValid_LeavesTheItemRunningAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct);

        await work.Store.RecoverExpiredLeasesAsync(At(60), 3, Ct);

        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.Stalls).Should().Be((WorkItemStatus.Running, 0));
    }

    [Fact]
    public async Task RecoverExpiredLeasesAsync_Requeued_ClearsTheClaimAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct);

        await work.Store.RecoverExpiredLeasesAsync(At(61), 3, Ct);

        var item = await work.ItemAsync(id, Ct);
        (item.ClaimedBy, item.StartedAt, item.LeaseUntil).Should().Be((null, null, null));
        item.UpdatedAt.Should().Be(At(61));
    }

    [Fact]
    public async Task ReleaseAsync_QueuesTheItemWithoutAStallAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;

        await work.Store.ReleaseAsync(claimed.Attempt, At(5), Ct);

        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        (item.Status, item.Stalls, item.Attempt).Should().Be((WorkItemStatus.Queued, 0, 1));
        (item.ClaimedBy, item.LeaseUntil).Should().Be((null, null));
        (await work.Store.ClaimAsync("worker", null, At(6), Lease, Ct))!.Attempt.Should().Be(claimed.Attempt with { Attempt = 2 });
    }

    [Fact]
    public async Task ReleaseAsync_SameKeyAlreadyQueued_SupersedesTheReleasedItemAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        var running = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        var waiting = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);

        await work.Store.ReleaseAsync(running.Attempt, At(5), Ct);

        var released = await work.ItemAsync(running.Attempt.Id, Ct);
        (released.Status, released.Error, released.Stalls).Should().Be(
            (WorkItemStatus.Cancelled, EfWorkItemStore.SupersededError, 0));
        released.FinishedAt.Should().Be(At(5));
        (await work.ItemAsync(waiting, Ct)).Status.Should().Be(WorkItemStatus.Queued);
    }

    [Fact]
    public async Task RecoverExpiredLeasesAsync_SameKeyAlreadyQueued_SupersedesItAndStillRecoversTheRestAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        var keyed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        work.Clock.Now = At(1);
        var other = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        await work.Store.ClaimAsync("worker", null, At(1), Lease, Ct);
        var waiting = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);

        await work.Store.RecoverExpiredLeasesAsync(At(62), 3, Ct);

        var superseded = await work.ItemAsync(keyed.Attempt.Id, Ct);
        (superseded.Status, superseded.Error, superseded.Stalls).Should().Be(
            (WorkItemStatus.Cancelled, EfWorkItemStore.SupersededError, 1));
        (await work.ItemAsync(other, Ct)).Status.Should().Be(WorkItemStatus.Queued);
        (await work.ItemAsync(waiting, Ct)).Status.Should().Be(WorkItemStatus.Queued);
    }

    [Fact]
    public async Task RecoverExpiredLeasesAsync_TwoExpiredItemsShareAKey_RequeuesOneAndSupersedesTheOtherAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct);
        work.Clock.Now = At(1);
        await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        await work.Store.ClaimAsync("worker", null, At(1), Lease, Ct);

        await work.Store.RecoverExpiredLeasesAsync(At(62), 3, Ct);

        (await work.ItemsAsync(Ct)).Select(w => w.Status)
            .Should().BeEquivalentTo([WorkItemStatus.Queued, WorkItemStatus.Cancelled]);
    }

    [Fact]
    public async Task ClaimAsync_KindThisWorkerDoesNotKnow_LeavesItQueuedAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var unknown = Guid.NewGuid();
        var requestedAt = At(-10);
        const string payload = "{}";
        await using (var db = work.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "WorkItems" ("Id", "Kind", "Payload", "RequestedAt", "Status", "Attempt", "Stalls", "UpdatedAt", "ProgressDone")
                VALUES ({unknown}, 'FromANewerRelease', CAST({payload} AS jsonb), {requestedAt}, 'Queued', 0, 0, {requestedAt}, 0)
                """, Ct);
        }

        var known = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);

        (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!.Attempt.Id.Should().Be(known);
        (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct)).Should().BeNull();
        await using var check = work.CreateDbContext();
        (await check.Database
            .SqlQuery<string>($"""SELECT "Status" AS "Value" FROM "WorkItems" WHERE "Id" = {unknown}""")
            .ToListAsync(Ct)).Should().Equal("Queued");
    }

    [Fact]
    public async Task SucceedAsync_StoresTheResultAndTheFinalProgressAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;

        await work.Store.SucceedAsync(claimed.Attempt, Halfway, /*lang=json,strict*/ """{"deleted": 3}""", At(9), Ct);

        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        item.Status.Should().Be(WorkItemStatus.Succeeded);
        item.Result.Should().Be(/*lang=json,strict*/ """{"deleted": 3}""");
        (item.ProgressDone, item.ProgressTotal, item.ProgressNote).Should().Be((5L, 10L, "halfway"));
        (item.FinishedAt, item.UpdatedAt).Should().Be((At(9), At(9)));
    }

    [Fact]
    public async Task FailAsync_StoresTheErrorAndTheLastProgressButNoResultAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;

        await work.Store.FailAsync(claimed.Attempt, Halfway, "InvalidOperationException: boom", At(9), Ct);

        var item = await work.ItemAsync(claimed.Attempt.Id, Ct);
        item.Status.Should().Be(WorkItemStatus.Failed);
        item.Error.Should().Be("InvalidOperationException: boom");
        item.Result.Should().BeNull();
        (item.ProgressDone, item.ProgressTotal, item.ProgressNote).Should().Be((5L, 10L, "halfway"));
        (item.FinishedAt, item.UpdatedAt).Should().Be((At(9), At(9)));
    }

    [Fact]
    public async Task HeartbeatAsync_CancelRequested_ReportsItAndStillExtendsTheLeaseAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        work.Clock.Now = At(10);
        await work.CancelAsync(id, Ct);

        var heartbeat = await work.Store.HeartbeatAsync(claimed.Attempt, Halfway, At(30), Lease, Ct);

        heartbeat.Should().Be(HeartbeatOutcome.CancelRequested);
        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.LeaseUntil).Should().Be((WorkItemStatus.Running, At(90)));
        item.ProgressNote.Should().Be("halfway");
    }

    [Fact]
    public async Task CancelAsync_StoresTheLastProgressAndTheResultAsPartialAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        work.Clock.Now = At(5);
        await work.CancelAsync(id, Ct);

        await work.Store.CancelAsync(claimed.Attempt, Halfway, /*lang=json,strict*/ """{"deleted": 2}""", At(9), Ct);

        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.Error).Should().Be((WorkItemStatus.Cancelled, null));
        item.Result.Should().Be(/*lang=json,strict*/ """{"deleted": 2}""");
        (item.ProgressDone, item.ProgressTotal, item.ProgressNote).Should().Be((5L, 10L, "halfway"));
        (item.FinishedAt, item.UpdatedAt).Should().Be((At(9), At(9)));
    }

    [Fact]
    public async Task ReleaseAsync_CancelRequested_CancelsTheItemInsteadOfQueuingItAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        work.Clock.Now = At(3);
        await work.CancelAsync(id, Ct);

        await work.Store.ReleaseAsync(claimed.Attempt, At(5), Ct);

        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.Error, item.Stalls).Should().Be((WorkItemStatus.Cancelled, null, 0));
        (item.FinishedAt, item.UpdatedAt).Should().Be((At(5), At(5)));
        (await work.Store.ClaimAsync("worker", null, At(6), Lease, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ReleaseAsync_CancelRequestedAndSameKeyQueued_CancelsWithoutCallingItSupersededAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        await work.CancelAsync(id, Ct);
        var waiting = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);

        await work.Store.ReleaseAsync(claimed.Attempt, At(5), Ct);

        (await work.ItemAsync(id, Ct)).Should().BeEquivalentTo(new { Status = WorkItemStatus.Cancelled, Error = (string?)null });
        (await work.ItemAsync(waiting, Ct)).Status.Should().Be(WorkItemStatus.Queued);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public async Task RecoverExpiredLeasesAsync_CancelRequested_CancelsTheItemEvenAtTheStallLimitAsync(int maxStalls)
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct);
        await work.CancelAsync(id, Ct);

        await work.Store.RecoverExpiredLeasesAsync(At(61), maxStalls, Ct);

        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.Error, item.Stalls).Should().Be((WorkItemStatus.Cancelled, null, 1));
        item.FinishedAt.Should().Be(At(61));
    }

    [Fact]
    public async Task DeleteFinishedBeforeAsync_DeletesOnlyItemsThatFinishedBeforeTheCutoffAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await FinishedAsync(work, At(10));
        var recent = await FinishedAsync(work, At(100));
        var queued = await work.EnqueueAsync(new UnkeyedPayload(3), Ct);

        var deleted = await work.Store.DeleteFinishedBeforeAsync(At(100), Ct);

        deleted.Should().Be(1);
        (await work.ItemsAsync(Ct)).Select(w => w.Id).Should().BeEquivalentTo([recent, queued]);
    }

    [Fact]
    public async Task IsDueAsync_FollowsTheLastFinishedItemWithTheKeyAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        (await work.Store.IsDueAsync("sweep", At(0), Ct)).Should().BeTrue();

        var id = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        (await work.Store.IsDueAsync("sweep", At(0), Ct)).Should().BeFalse();

        var claimed = (await work.Store.ClaimAsync("worker", null, At(0), Lease, Ct))!;
        (await work.Store.IsDueAsync("sweep", At(0), Ct)).Should().BeFalse();

        await work.Store.SucceedAsync(claimed.Attempt, WorkProgress.None, null, At(50), Ct);
        (await work.Store.IsDueAsync("sweep", At(49), Ct)).Should().BeFalse();
        (await work.Store.IsDueAsync("sweep", At(50), Ct)).Should().BeTrue();
        id.Should().Be(claimed.Attempt.Id);
    }

    private static async Task<Guid> FinishedAsync(WorkDatabase work, Instant finishedAt)
    {
        await work.EnqueueAsync(new UnkeyedPayload(0), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, finishedAt, Lease, Ct))!;
        await work.Store.SucceedAsync(claimed.Attempt, WorkProgress.None, null, finishedAt, Ct);
        return claimed.Attempt.Id;
    }
}
