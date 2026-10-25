using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NodaTime;
using NSubstitute;

namespace Anichron.Core.Tests.Unit.Work;

public sealed class WorkQueueTests
{
    private sealed record Unkeyed(int Number) : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.WorkItemRetention;
    }

    private sealed record Keyed(string Key) : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.TokenCleanup;

        string IWorkPayload.DedupKey => Key;
    }

    private sealed record Scoped(Guid ConfigId) : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.TokenCleanup;

        Guid? IWorkPayload.StorageConfigId => ConfigId;
    }

    private static readonly Instant Now = Instant.FromUtc(2026, 10, 18, 9, 0);
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly DbContextOptions<AnichronDbContext> options = AnichronDbContexts.InMemoryOptions();
    private readonly IGuidFactory guids = Substitute.For<IGuidFactory>();
    private readonly IClock clock = Substitute.For<IClock>();

    public WorkQueueTests()
    {
        clock.GetCurrentInstant().Returns(Now);
        guids.NewGuid().Returns(_ => Guid.NewGuid());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AnichronDbContext Context(IInterceptor? interceptor = null)
        => interceptor is null
            ? new AnichronDbContext(options)
            : new AnichronDbContext(new DbContextOptionsBuilder<AnichronDbContext>(options)
                .AddInterceptors(interceptor)
                .Options);

    private async Task<Guid> EnqueueAsync<TPayload>(TPayload payload, Guid? requestedBy = null, IInterceptor? interceptor = null)
        where TPayload : IWorkPayload
    {
        await using var db = Context(interceptor);
        return await new EfWorkQueue(db, clock, guids).EnqueueAsync(payload, requestedBy, Ct);
    }

    private sealed class LostInsertRaces(int losses) : SaveChangesInterceptor
    {
        private int saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ++saves > losses
                ? ValueTask.FromResult(result)
                : throw DatabaseErrors.UniqueViolation(WorkItemIndexNames.QueuedDedupKeyUnique);
    }

    private async Task<List<WorkItem>> ItemsAsync()
    {
        await using var db = Context();
        return await db.WorkItems.ToListAsync(Ct);
    }

    private async Task SetStatusAsync(Guid id, WorkItemStatus status)
    {
        await using var db = Context();
        (await db.WorkItems.SingleAsync(w => w.Id == id, Ct)).Status = status;
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task EnqueueAsync_AddsAQueuedItemDescribingTheRequestAsync()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        guids.NewGuid().Returns(id);

        var returned = await EnqueueAsync(new Keyed("sweep"), AdminId);

        returned.Should().Be(id);
        var item = (await ItemsAsync()).Should().ContainSingle().Which;
        item.Should().BeEquivalentTo(new
        {
            Id = id,
            Kind = WorkKind.TokenCleanup,
            Payload = /*lang=json,strict*/ """{"key":"sweep"}""",
            DedupKey = "sweep",
            RequestedBy = (Guid?)AdminId,
            RequestedAt = Now,
            Status = WorkItemStatus.Queued,
            Attempt = 0,
            Stalls = 0,
            UpdatedAt = Now,
        });
    }

    [Fact]
    public async Task EnqueueAsync_Unkeyed_TakesTheKindFromThePayloadTypeAndKeepsNoKeyAsync()
    {
        await EnqueueAsync(new Unkeyed(1));

        var item = (await ItemsAsync()).Should().ContainSingle().Which;
        (item.Kind, item.DedupKey, item.RequestedBy, item.StorageConfigId)
            .Should().Be((WorkKind.WorkItemRetention, null, null, null));
    }

    [Fact]
    public async Task EnqueueAsync_ScopedPayload_StoresItsStorageConfigAsync()
    {
        var configId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await EnqueueAsync(new Scoped(configId));

        (await ItemsAsync()).Should().ContainSingle().Which.StorageConfigId.Should().Be(configId);
    }

    [Fact]
    public async Task EnqueueAsync_UnkeyedTwice_AddsTwoItemsAsync()
    {
        await EnqueueAsync(new Unkeyed(1));
        await EnqueueAsync(new Unkeyed(1));

        (await ItemsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task EnqueueAsync_KeyAlreadyQueued_ReturnsThatItemAndAddsNothingAsync()
    {
        var waiting = await EnqueueAsync(new Keyed("sweep"));

        var returned = await EnqueueAsync(new Keyed("sweep"));

        returned.Should().Be(waiting);
        (await ItemsAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData(WorkItemStatus.Running)]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public async Task EnqueueAsync_KeyHeldOnlyByAnItemNoLongerQueued_AddsANewItemAsync(WorkItemStatus status)
    {
        var earlier = await EnqueueAsync(new Keyed("sweep"));
        await SetStatusAsync(earlier, status);

        var returned = await EnqueueAsync(new Keyed("sweep"));

        returned.Should().NotBe(earlier);
        (await ItemsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task EnqueueAsync_AnotherKeyQueued_AddsANewItemAsync()
    {
        var other = await EnqueueAsync(new Keyed("delete:a"));

        var returned = await EnqueueAsync(new Keyed("delete:b"));

        returned.Should().NotBe(other);
        (await ItemsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task EnqueueAsync_LosesTheInsertRaceTwice_InsertsOnTheThirdAttemptAsync()
    {
        var id = await EnqueueAsync(new Keyed("crawl"), interceptor: new LostInsertRaces(losses: 2));

        (await ItemsAsync()).Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task EnqueueAsync_LosesTheInsertRaceThreeTimes_ThrowsNamingTheKeyAsync()
    {
        var act = () => EnqueueAsync(new Keyed("crawl"), interceptor: new LostInsertRaces(losses: 3));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Enqueueing work with dedup key 'crawl' lost the insert race 3 times.");
        (await ItemsAsync()).Should().BeEmpty();
    }
}
