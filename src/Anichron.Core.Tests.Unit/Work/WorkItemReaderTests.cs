using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Work;

public sealed class WorkItemReaderTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 10, 18, 9, 0);
    private static readonly Guid First = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Third = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly string database = Guid.NewGuid().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Instant At(int seconds) => Start + Duration.FromSeconds(seconds);

    private AnichronDbContext Context() => AnichronDbContexts.InMemory(database);

    private EfWorkItemReader Reader() => new(Context());

    private async Task SeedAsync(params WorkItem[] items)
    {
        await using var db = Context();
        db.WorkItems.AddRange(items);
        await db.SaveChangesAsync(Ct);
    }

    private static WorkItem Item(
        Guid id,
        int requestedAt = 0,
        int? updatedAt = null,
        WorkItemStatus status = WorkItemStatus.Queued,
        WorkKind kind = WorkKind.TokenCleanup)
        => new()
        {
            Id = id,
            Kind = kind,
            Payload = "{}",
            RequestedAt = At(requestedAt),
            Status = status,
            UpdatedAt = At(updatedAt ?? requestedAt),
        };

    private static IEnumerable<Guid> Ids(IEnumerable<WorkItemView> views) => views.Select(v => v.Id);

    // ==========================================================================
    // ListAsync
    // ==========================================================================

    [Fact]
    public async Task ListAsync_NoFilter_ReturnsEveryItemNewestRequestFirstAsync()
    {
        await SeedAsync(Item(First, requestedAt: 1), Item(Second, requestedAt: 3), Item(Third, requestedAt: 2));

        var page = await Reader().ListAsync(new WorkItemQuery(null, null, Skip: 0, Take: 10), Ct);

        Ids(page.Items).Should().Equal(Second, Third, First);
        page.Total.Should().Be(3);
    }

    [Fact]
    public async Task ListAsync_OrdersByRequestTimeNotByLastChangeAsync()
    {
        await SeedAsync(Item(First, requestedAt: 1, updatedAt: 50), Item(Second, requestedAt: 2, updatedAt: 2));

        var page = await Reader().ListAsync(new WorkItemQuery(null, null, Skip: 0, Take: 10), Ct);

        Ids(page.Items).Should().Equal(Second, First);
    }

    [Fact]
    public async Task ListAsync_SameRequestTime_PutsTheLargerIdFirstAsync()
    {
        await SeedAsync(Item(First, requestedAt: 1), Item(Second, requestedAt: 1));

        var page = await Reader().ListAsync(new WorkItemQuery(null, null, Skip: 0, Take: 10), Ct);

        Ids(page.Items).Should().Equal(Second, First);
    }

    [Fact]
    public async Task ListAsync_SkipAndTake_ReturnThatPageAndTheFullTotalAsync()
    {
        await SeedAsync(Item(First, requestedAt: 1), Item(Second, requestedAt: 2), Item(Third, requestedAt: 3));

        var page = await Reader().ListAsync(new WorkItemQuery(null, null, Skip: 1, Take: 1), Ct);

        Ids(page.Items).Should().Equal(Second);
        page.Total.Should().Be(3);
    }

    [Fact]
    public async Task ListAsync_StatusFilter_ReturnsAndCountsOnlyThatStatusAsync()
    {
        await SeedAsync(
            Item(First, requestedAt: 1, status: WorkItemStatus.Running),
            Item(Second, requestedAt: 2, status: WorkItemStatus.Failed),
            Item(Third, requestedAt: 3, status: WorkItemStatus.Running));

        var page = await Reader().ListAsync(new WorkItemQuery(WorkItemStatus.Running, null, Skip: 0, Take: 10), Ct);

        Ids(page.Items).Should().Equal(Third, First);
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_KindFilter_ReturnsAndCountsOnlyThatKindAsync()
    {
        await SeedAsync(
            Item(First, requestedAt: 1, kind: WorkKind.Crawl),
            Item(Second, requestedAt: 2, kind: WorkKind.TokenCleanup),
            Item(Third, requestedAt: 3, kind: WorkKind.Crawl));

        var page = await Reader().ListAsync(new WorkItemQuery(null, WorkKind.Crawl, Skip: 0, Take: 10), Ct);

        Ids(page.Items).Should().Equal(Third, First);
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_StatusAndKindFilters_BothApplyAsync()
    {
        await SeedAsync(
            Item(First, requestedAt: 1, status: WorkItemStatus.Running, kind: WorkKind.Crawl),
            Item(Second, requestedAt: 2, status: WorkItemStatus.Running, kind: WorkKind.TokenCleanup),
            Item(Third, requestedAt: 3, status: WorkItemStatus.Failed, kind: WorkKind.Crawl));

        var page = await Reader().ListAsync(new WorkItemQuery(WorkItemStatus.Running, WorkKind.Crawl, Skip: 0, Take: 10), Ct);

        Ids(page.Items).Should().Equal(First);
        page.Total.Should().Be(1);
    }

    // ==========================================================================
    // FindAsync
    // ==========================================================================

    [Fact]
    public async Task FindAsync_KnownId_ReturnsEveryShownFieldAsync()
    {
        var configId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var adminId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        await SeedAsync(new WorkItem
        {
            Id = First,
            Kind = WorkKind.Crawl,
            Payload = /*lang=json,strict*/ """{"storageConfigId": "44444444-4444-4444-4444-444444444444"}""",
            DedupKey = "crawl:44444444",
            StorageConfigId = configId,
            RequestedBy = adminId,
            RequestedAt = At(1),
            Status = WorkItemStatus.Succeeded,
            Attempt = 2,
            Stalls = 1,
            ClaimedBy = "all@host",
            StartedAt = At(2),
            LeaseUntil = At(62),
            FinishedAt = At(9),
            UpdatedAt = At(9),
            ProgressDone = 7,
            ProgressTotal = 8,
            ProgressNote = "7 of 8",
            Result = /*lang=json,strict*/ """{"found": 8}""",
            Error = "an earlier error",
        });

        var view = await Reader().FindAsync(First, Ct);

        view.Should().Be(new WorkItemView(
            First, WorkKind.Crawl, configId, adminId, At(1), WorkItemStatus.Succeeded, 2, 1, "all@host",
            At(2), At(9), At(9), new WorkProgress(7, 8, "7 of 8"), /*lang=json,strict*/ """{"found": 8}""", "an earlier error"));
    }

    [Fact]
    public async Task FindAsync_UnknownId_ReturnsNullAsync()
    {
        await SeedAsync(Item(First));

        (await Reader().FindAsync(Second, Ct)).Should().BeNull();
    }

    // ==========================================================================
    // UpdatedSinceAsync
    // ==========================================================================

    [Fact]
    public async Task UpdatedSinceAsync_ReturnsItemsChangedStrictlyAfterInChangeOrderAsync()
    {
        await SeedAsync(
            Item(First, updatedAt: 12),
            Item(Second, updatedAt: 10),
            Item(Third, updatedAt: 11));

        var changed = await Reader().UpdatedSinceAsync(At(10), Ct);

        Ids(changed).Should().Equal(Third, First);
    }

    [Fact]
    public async Task UpdatedSinceAsync_SameChangeTime_PutsTheSmallerIdFirstAsync()
    {
        await SeedAsync(Item(Second, updatedAt: 11), Item(First, updatedAt: 11));

        var changed = await Reader().UpdatedSinceAsync(At(10), Ct);

        Ids(changed).Should().Equal(First, Second);
    }

    [Fact]
    public void UpdatedSinceAsync_OnPostgres_FiltersAndOrdersInTheQuery()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        var sql = EfWorkItemReader.UpdatedSinceQuery(db, At(10)).ToQueryString();

        sql.Should().Contain("""WHERE w."UpdatedAt" > @since""")
           .And.Contain("""ORDER BY w."UpdatedAt", w."Id" """.TrimEnd());
    }

    // ==========================================================================
    // LatestUpdateAsync
    // ==========================================================================

    [Fact]
    public async Task LatestUpdateAsync_ReturnsTheNewestChangeTimeAsync()
    {
        await SeedAsync(Item(First, updatedAt: 12), Item(Second, updatedAt: 30), Item(Third, updatedAt: 11));

        (await Reader().LatestUpdateAsync(Ct)).Should().Be(At(30));
    }

    [Fact]
    public async Task LatestUpdateAsync_NoItems_ReturnsNullAsync()
        => (await Reader().LatestUpdateAsync(Ct)).Should().BeNull();
}
