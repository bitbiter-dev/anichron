using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class ProxyOrphanReaderTests : IAsyncDisposable
{
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid OtherSweepId = Guid.Parse("51000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigA = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigB = Guid.Parse("c2000000-0000-0000-0000-000000000000");
    private static readonly Instant Written = Instant.FromUtc(2026, 10, 27, 9, 0);
    private static readonly Instant Processed = Instant.FromUtc(2026, 10, 27, 12, 0);

    private readonly AnichronDbContext db = AnichronDbContexts.InMemory();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EfProxyOrphanReader Reader => new(db);

    public async ValueTask DisposeAsync() => await db.DisposeAsync();

    private static ProxyOrphan Orphan(
        int n, string path, Guid? configId = null, ProxyDeletionOutcome? outcome = null, Guid? sweepId = null)
        => new()
        {
            Id = Id(n),
            SweepId = sweepId ?? SweepId,
            ProxyPath = path,
            StorageConfigId = configId,
            SizeBytes = 100 + n,
            LastWriteTime = Written,
            DeletionOutcome = outcome,
            ProcessedAt = outcome is null ? null : Processed,
        };

    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private async Task SeedAsync(params ProxyOrphan[] orphans)
    {
        db.ProxyOrphans.AddRange(orphans);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
    }

    // ==========================================================================
    // ListAsync
    // ==========================================================================

    [Fact]
    public async Task ListAsync_ReadsTheSweepsOrphansByPathAsync()
    {
        await SeedAsync(
            Orphan(1, "cd/x/thumbnail.jpg", ConfigA, ProxyDeletionOutcome.Deleted),
            Orphan(2, "ab/x/thumbnail.jpg"),
            Orphan(3, "ab/x/preview.jpg", sweepId: OtherSweepId));

        var page = await Reader.ListAsync(new ProxyOrphanQuery(SweepId, null, null, 0, 10), Ct);

        page.Total.Should().Be(2);
        page.Items.Should().Equal(
            new ProxyOrphanView(Id(2), "ab/x/thumbnail.jpg", null, 102, Written, null, null),
            new ProxyOrphanView(Id(1), "cd/x/thumbnail.jpg", ConfigA, 101, Written, ProxyDeletionOutcome.Deleted, Processed));
    }

    [Fact]
    public async Task ListAsync_FiltersByOutcomeAndConfigAsync()
    {
        await SeedAsync(
            Orphan(1, "a", ConfigA, ProxyDeletionOutcome.Deleted),
            Orphan(2, "b", ConfigA, ProxyDeletionOutcome.Missing),
            Orphan(3, "c", ConfigB, ProxyDeletionOutcome.Deleted),
            Orphan(4, "d", ConfigA, ProxyDeletionOutcome.Deleted));

        var page = await Reader.ListAsync(new ProxyOrphanQuery(SweepId, ProxyDeletionOutcome.Deleted, ConfigA, 0, 10), Ct);

        page.Items.Select(o => o.Id).Should().Equal(Id(1), Id(4));
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_SkipsAndTakesThePageButCountsEveryMatchAsync()
    {
        await SeedAsync(Orphan(1, "a"), Orphan(2, "b"), Orphan(3, "c"), Orphan(4, "d"));

        var page = await Reader.ListAsync(new ProxyOrphanQuery(SweepId, null, null, 1, 2), Ct);

        page.Items.Select(o => o.Id).Should().Equal(Id(2), Id(3));
        page.Total.Should().Be(4);
    }

    // ==========================================================================
    // FindAsync
    // ==========================================================================

    [Fact]
    public async Task FindAsync_ReadsOnlyTheSweepsOrphansAmongTheIdsAsync()
    {
        await SeedAsync(
            Orphan(1, "a", ConfigA),
            Orphan(2, "b"),
            Orphan(3, "c", ConfigB, sweepId: OtherSweepId),
            Orphan(4, "d", ConfigB));

        var found = await Reader.FindAsync(SweepId, [Id(1), Id(2), Id(3), Id(9)], Ct);

        found.Should().BeEquivalentTo([new ProxyOrphanScope(Id(1), ConfigA), new ProxyOrphanScope(Id(2), null)]);
    }

    // ==========================================================================
    // PendingConfigsAsync
    // ==========================================================================

    [Fact]
    public async Task PendingConfigsAsync_ReadsEachConfigOfTheSweepsUnprocessedOrphansOnceAsync()
    {
        await SeedAsync(
            Orphan(1, "a", ConfigA),
            Orphan(2, "b", ConfigA),
            Orphan(3, "c"),
            Orphan(4, "d", ConfigB, ProxyDeletionOutcome.Deleted),
            Orphan(5, "e", ConfigB, sweepId: OtherSweepId));

        var configs = await Reader.PendingConfigsAsync(SweepId, Ct);

        configs.Should().BeEquivalentTo(new Guid?[] { ConfigA, null });
    }
}
