using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Data;
using Anichron.Worker.ProxySweeps;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Tests.Unit.ProxySweeps;

public sealed class EfProxyOrphanStoreTests : IAsyncDisposable
{
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid OtherSweepId = Guid.Parse("51000000-0000-0000-0000-000000000000");
    private static readonly Guid LiveConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid OtherLiveConfigId = Guid.Parse("c2000000-0000-0000-0000-000000000000");
    private static readonly Guid GoneConfigId = Guid.Parse("c9000000-0000-0000-0000-000000000000");

    private readonly AnichronDbContext db = AnichronDbContexts.InMemory(freshModel: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EfProxyOrphanStore Store => new(db, Substitute.For<IGuidFactory>());

    public async ValueTask DisposeAsync() => await db.DisposeAsync();

    private async Task SeedProxiesAsync(params (string Path, bool SoftDeleted)[] proxies)
    {
        db.StorageConfigs.Add(new UserStorageConfig { Id = LiveConfigId, UserId = Guid.NewGuid(), RootPath = "/data/originals/mira" });
        foreach (var (path, softDeleted) in proxies)
        {
            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                StorageConfigId = LiveConfigId,
                FilePath = path,
                FileName = path,
                ContentHash = path,
                IsSoftDeleted = softDeleted,
            };
            db.MediaAssets.Add(asset);
            db.ProxyFiles.Add(new ProxyFile { Id = Guid.NewGuid(), AssetId = asset.Id, ProxyPath = path });
        }

        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
    }

    private static ProxyOrphan Orphan(
        string id, Guid? configId, Guid? sweepId = null, ProxyDeletionOutcome? outcome = null, string? path = null, long size = 0)
        => new()
        {
            Id = Guid.Parse(id),
            SweepId = sweepId ?? SweepId,
            ProxyPath = path ?? $"ab/{id}/thumbnail.jpg",
            StorageConfigId = configId,
            SizeBytes = size,
            DeletionOutcome = outcome,
        };

    private async Task SeedOrphansAsync(params ProxyOrphan[] orphans)
    {
        db.StorageConfigs.AddRange(
            new UserStorageConfig { Id = LiveConfigId, UserId = Guid.NewGuid(), RootPath = "/data/originals/mira" },
            new UserStorageConfig { Id = OtherLiveConfigId, UserId = Guid.NewGuid(), RootPath = "/data/originals/alice" });
        db.ProxyOrphans.AddRange(orphans);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
    }

    private async Task<IEnumerable<Guid>> PendingAsync(Guid? configId, IReadOnlyList<Guid>? orphanIds = null, int take = 100)
        => (await Store.GetPendingAsync(new ProxyDeletionPayload(SweepId, configId, orphanIds), take, Ct)).Select(o => o.Id);

    // ==========================================================================
    // ReferencedUnderAsync
    // ==========================================================================

    [Fact]
    public async Task ReferencedUnderAsync_ReadsThePathsInTheShardSoftDeletedAssetsIncludedAsync()
    {
        await SeedProxiesAsync(
            ("ab/c-1/thumbnail.jpg", false),
            ("ab/c-2/preview.jpg", true),
            ("abc/c-3/thumbnail.jpg", false),
            ("cd/c-4/thumbnail.jpg", false));

        var referenced = await Store.ReferencedUnderAsync("ab", Ct);

        referenced.Should().BeEquivalentTo("ab/c-1/thumbnail.jpg", "ab/c-2/preview.jpg");
    }

    [Fact]
    public async Task ReferencedUnderAsync_ComparesPathsByOrdinalAsync()
    {
        await SeedProxiesAsync(("ab/c-1/thumbnail.jpg", false));

        var referenced = await Store.ReferencedUnderAsync("ab", Ct);

        referenced.Contains("ab/c-1/THUMBNAIL.jpg").Should().BeFalse();
    }

    [Fact]
    public void ReferencedUnderQuery_IsAPrefixMatchOnThePath()
    {
        using var npgsql = AnichronDbContexts.NpgsqlModel();

        var sql = EfProxyOrphanStore.ReferencedUnderQuery(npgsql, "ab").ToQueryString();

        sql.Should().Contain("""WHERE p."ProxyPath" LIKE """).And.NotContain("IsSoftDeleted");
    }

    // ==========================================================================
    // IsReferencedAsync
    // ==========================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IsReferencedAsync_AnyRowWithThePath_IsTrueAsync(bool softDeleted)
    {
        await SeedProxiesAsync(("ab/c-1/thumbnail.jpg", softDeleted));

        (await Store.IsReferencedAsync("ab/c-1/thumbnail.jpg", Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task IsReferencedAsync_NoRowWithThePath_IsFalseAsync()
    {
        await SeedProxiesAsync(("ab/c-1/thumbnail.jpg", false));

        (await Store.IsReferencedAsync("ab/c-1/preview.jpg", Ct)).Should().BeFalse();
    }

    // ==========================================================================
    // SummariseAsync
    // ==========================================================================

    [Fact]
    public async Task SummariseAsync_CountsTheSweepsReportAsync()
    {
        await SeedOrphansAsync(
            Orphan("00000000-0000-0000-0000-000000000001", null, path: "stray.txt", size: 1),
            Orphan("00000000-0000-0000-0000-000000000002", LiveConfigId, outcome: ProxyDeletionOutcome.Deleted, size: 20),
            Orphan("00000000-0000-0000-0000-000000000003", LiveConfigId, path: "ab/x/preview.jpg.0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f.tmp", size: 300),
            Orphan("00000000-0000-0000-0000-000000000004", GoneConfigId, size: 4000),
            Orphan("00000000-0000-0000-0000-000000000005", GoneConfigId, size: 50000),
            Orphan("00000000-0000-0000-0000-000000000006", Guid.Parse("c8000000-0000-0000-0000-000000000000"), path: "cd/x/thumbnail.tmp.jpg", size: 600000),
            Orphan("00000000-0000-0000-0000-000000000007", Guid.Parse("c7000000-0000-0000-0000-000000000000"), sweepId: OtherSweepId, path: "ef/x/thumbnail.jpg.tmp", size: 7000000));

        var summary = await Store.SummariseAsync(SweepId, Ct);

        summary.Should().Be(new ProxySweepResult(Orphans: 6, Bytes: 654321, TemporaryFiles: 1, ConfigsGone: 2));
    }

    [Fact]
    public async Task SummariseAsync_EmptyReport_IsAllZeroAsync()
    {
        await SeedOrphansAsync();

        (await Store.SummariseAsync(SweepId, Ct)).Should().Be(new ProxySweepResult(0, 0, 0, 0));
    }

    // ==========================================================================
    // GetPendingAsync
    // ==========================================================================

    [Fact]
    public async Task GetPendingAsync_Scoped_ReadsTheUnprocessedOrphansOfItsConfigInTheSweepAsync()
    {
        await SeedOrphansAsync(
            Orphan("00000000-0000-0000-0000-000000000001", LiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000002", LiveConfigId, outcome: ProxyDeletionOutcome.Deleted),
            Orphan("00000000-0000-0000-0000-000000000003", OtherLiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000004", LiveConfigId, sweepId: OtherSweepId),
            Orphan("00000000-0000-0000-0000-000000000005", null),
            Orphan("00000000-0000-0000-0000-000000000006", LiveConfigId));

        (await PendingAsync(LiveConfigId)).Should().Equal(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000006"));
    }

    [Fact]
    public async Task GetPendingAsync_Unscoped_ReadsOnlyOrphansOfNoExistingConfigAsync()
    {
        await SeedOrphansAsync(
            Orphan("00000000-0000-0000-0000-000000000001", GoneConfigId),
            Orphan("00000000-0000-0000-0000-000000000002", LiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000003", null),
            Orphan("00000000-0000-0000-0000-000000000004", GoneConfigId, outcome: ProxyDeletionOutcome.Missing));

        (await PendingAsync(null)).Should().Equal(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000003"));
    }

    [Fact]
    public async Task GetPendingAsync_Unscoped_SelectedOrphanOfALiveConfig_IsNotReadAsync()
    {
        await SeedOrphansAsync(
            Orphan("00000000-0000-0000-0000-000000000001", GoneConfigId),
            Orphan("00000000-0000-0000-0000-000000000002", LiveConfigId));

        (await PendingAsync(null, [Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002")]))
            .Should().Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public async Task GetPendingAsync_Selection_ReadsOnlyTheSelectedOrphansAsync()
    {
        await SeedOrphansAsync(
            Orphan("00000000-0000-0000-0000-000000000001", LiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000002", LiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000003", LiveConfigId));

        (await PendingAsync(LiveConfigId, [Guid.Parse("00000000-0000-0000-0000-000000000003"), Guid.Parse("00000000-0000-0000-0000-000000000001")]))
            .Should().Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000003"));
    }

    [Fact]
    public async Task GetPendingAsync_ReadsAtMostTheBatchInIdOrderAsync()
    {
        await SeedOrphansAsync(
            Orphan("00000000-0000-0000-0000-000000000003", LiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000001", LiveConfigId),
            Orphan("00000000-0000-0000-0000-000000000002", LiveConfigId));

        (await PendingAsync(LiveConfigId, take: 2)).Should().Equal(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"));
    }

    // ==========================================================================
    // ForgetLoaded
    // ==========================================================================

    [Fact]
    public async Task GetPendingAsync_TracksTheOrphansSoAnOutcomeSetOnOneIsSavedAsync()
    {
        await SeedOrphansAsync(Orphan("00000000-0000-0000-0000-000000000001", LiveConfigId));
        var store = Store;
        var orphan = (await store.GetPendingAsync(new ProxyDeletionPayload(SweepId, LiveConfigId, null), 10, Ct)).Single();

        orphan.DeletionOutcome = ProxyDeletionOutcome.Deleted;
        await db.SaveChangesAsync(Ct);

        db.ChangeTracker.Clear();
        (await db.ProxyOrphans.SingleAsync(Ct)).DeletionOutcome.Should().Be(ProxyDeletionOutcome.Deleted);
    }

    [Fact]
    public async Task ForgetLoaded_StopsTrackingTheOrphansReadAsync()
    {
        await SeedOrphansAsync(Orphan("00000000-0000-0000-0000-000000000001", LiveConfigId));
        var store = Store;
        _ = await store.GetPendingAsync(new ProxyDeletionPayload(SweepId, LiveConfigId, null), 10, Ct);

        store.ForgetLoaded();

        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
