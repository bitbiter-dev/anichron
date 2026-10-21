using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Work;

namespace Anichron.Tests.Integration.Work;

public sealed class WorkItemCancellerTests(PostgresServer server)
{
    private static readonly Duration Lease = Duration.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Instant At(int seconds) => WorkDatabase.Start + Duration.FromSeconds(seconds);

    [Fact]
    public async Task CancelAsync_QueuedItem_CancelsItAtOnceSoItNeverStartsAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        work.Clock.Now = At(5);

        var outcome = await work.CancelAsync(id, Ct);

        outcome.Should().Be(CancelOutcome.Cancelled);
        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.Error).Should().Be((WorkItemStatus.Cancelled, null));
        (item.CancelRequestedAt, item.FinishedAt, item.UpdatedAt).Should().Be((At(5), At(5), At(5)));
        (await work.Store.ClaimAsync("worker", null, At(6), Lease, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task CancelAsync_RunningItem_RecordsTheRequestAndLeavesItRunningAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        await work.Store.ClaimAsync("worker", null, At(1), Lease, Ct);
        work.Clock.Now = At(5);

        var outcome = await work.CancelAsync(id, Ct);

        outcome.Should().Be(CancelOutcome.Requested);
        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.FinishedAt).Should().Be((WorkItemStatus.Running, null));
        (item.CancelRequestedAt, item.UpdatedAt).Should().Be((At(5), At(5)));
    }

    [Fact]
    public async Task CancelAsync_RunningItemAlreadyRequested_KeepsTheFirstRequestAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        await work.Store.ClaimAsync("worker", null, At(1), Lease, Ct);
        work.Clock.Now = At(5);
        await work.CancelAsync(id, Ct);
        work.Clock.Now = At(8);

        var outcome = await work.CancelAsync(id, Ct);

        outcome.Should().Be(CancelOutcome.Requested);
        (await work.ItemAsync(id, Ct)).CancelRequestedAt.Should().Be(At(5));
    }

    [Theory]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public async Task CancelAsync_FinishedItem_ReportsItFinishedAndChangesNothingAsync(WorkItemStatus finished)
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var id = await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", null, At(1), Lease, Ct))!;
        if (finished == WorkItemStatus.Succeeded)
            await work.Store.SucceedAsync(claimed.Attempt, WorkProgress.None, null, At(2), Ct);
        else if (finished == WorkItemStatus.Failed)
            await work.Store.FailAsync(claimed.Attempt, WorkProgress.None, "boom", At(2), Ct);
        else
            await work.Store.CancelAsync(claimed.Attempt, WorkProgress.None, null, At(2), Ct);
        work.Clock.Now = At(5);

        var outcome = await work.CancelAsync(id, Ct);

        outcome.Should().Be(CancelOutcome.Finished);
        var item = await work.ItemAsync(id, Ct);
        (item.Status, item.CancelRequestedAt, item.UpdatedAt).Should().Be((finished, null, At(2)));
    }

    [Fact]
    public async Task CancelAsync_UnknownId_ReportsNotFoundAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);

        (await work.CancelAsync(Guid.NewGuid(), Ct)).Should().Be(CancelOutcome.NotFound);
    }

    [Fact]
    public async Task CancelAsync_RacingAClaim_NeverLosesTheCancelAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);

        for (var round = 0; round < 40; round++)
        {
            var id = await work.EnqueueAsync(new UnkeyedPayload(round), Ct);
            var claim = Task.Run(() => work.Store.ClaimAsync("worker", null, At(round), Lease, Ct), Ct);
            var cancel = Task.Run(() => work.CancelAsync(id, Ct), Ct);
            var (claimed, outcome) = (await claim, await cancel);

            if (claimed is null)
            {
                outcome.Should().Be(CancelOutcome.Cancelled);
                (await work.ItemAsync(id, Ct)).Status.Should().Be(WorkItemStatus.Cancelled);
            }
            else
            {
                outcome.Should().Be(CancelOutcome.Requested);
                (await work.Store.HeartbeatAsync(claimed.Attempt, WorkProgress.None, At(round), Lease, Ct))
                    .Should().Be(HeartbeatOutcome.CancelRequested);
                await work.Store.CancelAsync(claimed.Attempt, WorkProgress.None, null, At(round), Ct);
            }
        }
    }
}
