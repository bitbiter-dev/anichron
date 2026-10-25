using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Work;

public sealed class EfWorkItemStoreTests
{
    private sealed class InMemoryContexts(DbContextOptions<AnichronDbContext> options) : IDbContextFactory<AnichronDbContext>
    {
        public AnichronDbContext CreateDbContext() => new(options);
    }

    private static readonly Instant Cutoff = Instant.FromUtc(2026, 10, 18, 9, 0);

    private readonly InMemoryContexts contexts = new(AnichronDbContexts.InMemoryOptions());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task AddAsync(string key, WorkItemStatus status, Instant? finishedAt = null)
    {
        await using var db = contexts.CreateDbContext();
        db.WorkItems.Add(new WorkItem
        {
            Id = Guid.NewGuid(),
            Kind = WorkKind.TokenCleanup,
            Payload = "{}",
            DedupKey = key,
            Status = status,
            FinishedAt = finishedAt,
        });
        await db.SaveChangesAsync(Ct);
    }

    private Task<bool> IsDueAsync() => new EfWorkItemStore(contexts).IsDueAsync("token-cleanup", Cutoff, Ct);

    [Fact]
    public async Task IsDueAsync_NeverRan_IsDueAsync()
        => (await IsDueAsync()).Should().BeTrue();

    [Theory]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Running)]
    public async Task IsDueAsync_AnItemIsPending_IsNotDueAsync(WorkItemStatus status)
    {
        await AddAsync("token-cleanup", status);

        (await IsDueAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkItemStatus.Succeeded)]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    public async Task IsDueAsync_FinishedAfterTheCutoff_IsNotDueAsync(WorkItemStatus status)
    {
        await AddAsync("token-cleanup", status, Cutoff + Duration.FromTicks(1));

        (await IsDueAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task IsDueAsync_LastFinishedAtOrBeforeTheCutoff_IsDueAsync(long ticksEarlier)
    {
        await AddAsync("token-cleanup", WorkItemStatus.Succeeded, Cutoff - Duration.FromTicks(ticksEarlier));

        (await IsDueAsync()).Should().BeTrue();
    }

    [Theory]
    [InlineData(WorkItemStatus.Queued)]
    [InlineData(WorkItemStatus.Running)]
    public async Task IsDueAsync_OnlyAnotherKeyIsPending_IsDueAsync(WorkItemStatus status)
    {
        await AddAsync("work-item-retention", status);

        (await IsDueAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsDueAsync_AnotherKeyFinishedRecently_IsDueAsync()
    {
        await AddAsync("work-item-retention", WorkItemStatus.Succeeded, Cutoff + Duration.FromHours(1));

        (await IsDueAsync()).Should().BeTrue();
    }
}
