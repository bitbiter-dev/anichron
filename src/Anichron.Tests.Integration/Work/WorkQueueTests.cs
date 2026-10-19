using Anichron.Core.Domain;

namespace Anichron.Tests.Integration.Work;

public sealed class WorkQueueTests(PostgresServer server)
{
    private static readonly Duration Lease = Duration.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnqueueAsync_KeyAlreadyQueued_ReturnsTheWaitingItemAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var first = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);

        var second = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);

        second.Should().Be(first);
        (await work.ItemsAsync(Ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task EnqueueAsync_KeyOnlyRunning_AddsANewQueuedItemAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var running = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);
        await work.Store.ClaimAsync("worker", work.Clock.Now, Lease, Ct);

        var queued = await work.EnqueueAsync(new KeyedPayload("sweep"), Ct);

        queued.Should().NotBe(running);
        (await work.ItemAsync(queued, Ct)).Status.Should().Be(WorkItemStatus.Queued);
    }

    [Fact]
    public async Task EnqueueAsync_ConcurrentRequestsForOneKey_ProduceOneQueuedItemAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);

        var ids = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => work.EnqueueAsync(new KeyedPayload("token-cleanup"), Ct), Ct)));

        var item = (await work.ItemsAsync(Ct)).Should().ContainSingle().Which;
        ids.Should().AllBeEquivalentTo(item.Id);
    }

    [Fact]
    public async Task EnqueueAsync_DifferentKeys_StaySeparateItemsAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);

        await work.EnqueueAsync(new KeyedPayload("delete:a"), Ct);
        await work.EnqueueAsync(new KeyedPayload("delete:b"), Ct);

        (await work.ItemsAsync(Ct)).Select(w => w.DedupKey).Should().BeEquivalentTo("delete:a", "delete:b");
    }

    [Fact]
    public async Task EnqueueAsync_StoresTheKindAndPayloadAsJsonAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);

        var id = await work.EnqueueAsync(new UnkeyedPayload(7), Ct);

        var item = await work.ItemAsync(id, Ct);
        item.Kind.Should().Be(WorkKind.TokenCleanup);
        item.Payload.Should().Be(/*lang=json,strict*/ """{"number": 7}""");
        item.DedupKey.Should().BeNull();
        item.RequestedAt.Should().Be(WorkDatabase.Start);
    }
}
