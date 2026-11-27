using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Purges;
using Anichron.Worker.Tests.Unit.TestDoubles;
using Anichron.Worker.Work;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Purges;

public sealed class AssetPurgeTests
{
    private const string Shard = "/data/proxies/ab";
    private const string ContentDirectory = "/data/proxies/ab/c1000000000000000000000000000000-ab01";
    private const string Thumbnail = "ab/c1000000000000000000000000000000-ab01/thumbnail.jpg";
    private const string Preview = "ab/c1000000000000000000000000000000-ab01/preview.jpg";
    private static readonly Guid ConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");

    private readonly IAssetPurgeStore store = Substitute.For<IAssetPurgeStore>();
    private readonly IUserStorageConfigRepository configs = Substitute.For<IUserStorageConfigRepository>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly FakeLogger<AssetPurgeHandler> logger = new();

    public AssetPurgeTests()
        => configs.FindByIdAsync(ConfigId, Arg.Any<CancellationToken>())
            .Returns(new UserStorageConfig { Id = ConfigId, RootPath = "/data/originals/mira", IsActive = true });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Guid Asset(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static void File(MockFileSystem fileSystem, string relativePath)
        => fileSystem.AddFile($"/data/proxies/{relativePath}", new MockFileData(new byte[10]));

    private void Purges(params PurgedProxy[] purged)
        => store.DeleteSoftDeletedAsync(ConfigId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(purged);

    private void Present(int count)
        => store.CountPresentAsync(ConfigId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(count);

    private Task<object?> RunAsync(IFileSystem fileSystem, params Guid[] assetIds)
        => new AssetPurgeHandler(
                new ActiveStorageConfigs(configs, new FakeLogger<ActiveStorageConfigs>()),
                store,
                new ProxyFileRemover(new TwoLevelHexShardStrategy(), fileSystem, new FakeLogger<ProxyFileRemover>(logger.Collector)),
                logger)
            .RunAsync(new AssetPurgePayload(ConfigId, assetIds), new WorkRun(Guid.NewGuid(), progress), Ct);

    private async Task<AssetPurgeResult> PurgeAsync(IFileSystem fileSystem, params Guid[] assetIds)
        => (await RunAsync(fileSystem, assetIds)).Should().BeOfType<AssetPurgeResult>().Subject;

    // ==========================================================================
    // Scope
    // ==========================================================================

    [Fact]
    public async Task RunAsync_InactiveConfig_IsSkippedWithoutPurgingAsync()
    {
        configs.FindByIdAsync(ConfigId, Arg.Any<CancellationToken>())
            .Returns(new UserStorageConfig { Id = ConfigId, RootPath = "/data/originals/mira", IsActive = false });

        var result = await RunAsync(new MockFileSystem(), Asset(1));

        result.Should().Be(StorageConfigSkipped.MissingOrInactive);
        store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_DeletesThePayloadsIdsInItsConfigAsync()
    {
        await PurgeAsync(new MockFileSystem(), Asset(1), Asset(2));

        await store.Received(1).DeleteSoftDeletedAsync(
            ConfigId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Asset(1), Asset(2) })), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Rows
    // ==========================================================================

    [Fact]
    public async Task RunAsync_CountsPurgedRestoredAndAlreadyGoneRowsAsync()
    {
        Purges(new PurgedProxy(Asset(1), null), new PurgedProxy(Asset(2), null));
        Present(1);

        var result = await PurgeAsync(new MockFileSystem(), Asset(1), Asset(2), Asset(3), Asset(4), Asset(5));

        result.Should().Be(new AssetPurgeResult(
            Purged: 2, SkippedRestored: 1, AlreadyGone: 2, ProxyFilesDeleted: 0, ProxyFilesKept: 0, ProxyFilesFailed: 0));
    }

    [Fact]
    public async Task RunAsync_RetryOfAFinishedPurge_CountsEveryRowAsAlreadyGoneAsync()
    {
        var result = await PurgeAsync(new MockFileSystem(), Asset(1), Asset(2));

        result.Should().Be(new AssetPurgeResult(
            Purged: 0, SkippedRestored: 0, AlreadyGone: 2, ProxyFilesDeleted: 0, ProxyFilesKept: 0, ProxyFilesFailed: 0));
    }

    [Fact]
    public async Task RunAsync_LooksForRestoredRowsOnlyAfterTheDeleteAsync()
    {
        await PurgeAsync(new MockFileSystem(), Asset(1));

        Received.InOrder(() =>
        {
            _ = store.DeleteSoftDeletedAsync(ConfigId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
            _ = store.CountPresentAsync(
                ConfigId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { Asset(1) })), Arg.Any<CancellationToken>());
        });
    }

    // ==========================================================================
    // Proxy files
    // ==========================================================================

    [Fact]
    public async Task RunAsync_PurgedRowsFiles_AreDeletedWithTheirEmptiedFolderButNotTheShardAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        File(fileSystem, Preview);
        Purges(new PurgedProxy(Asset(1), Thumbnail), new PurgedProxy(Asset(1), Preview));

        var result = await PurgeAsync(fileSystem, Asset(1));

        fileSystem.Directory.Exists(ContentDirectory).Should().BeFalse();
        fileSystem.Directory.Exists(Shard).Should().BeTrue();
        result.Should().Be(new AssetPurgeResult(
            Purged: 1, SkippedRestored: 0, AlreadyGone: 0, ProxyFilesDeleted: 2, ProxyFilesKept: 0, ProxyFilesFailed: 0));
    }

    [Fact]
    public async Task RunAsync_FileAnotherRowReferences_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        File(fileSystem, Preview);
        Purges(new PurgedProxy(Asset(1), Thumbnail), new PurgedProxy(Asset(1), Preview));
        store.IsReferencedAsync(Thumbnail, Arg.Any<CancellationToken>()).Returns(true);

        var result = await PurgeAsync(fileSystem, Asset(1));

        fileSystem.File.Exists($"/data/proxies/{Thumbnail}").Should().BeTrue();
        fileSystem.File.Exists($"/data/proxies/{Preview}").Should().BeFalse();
        (result.ProxyFilesDeleted, result.ProxyFilesKept).Should().Be((1, 1));
    }

    [Fact]
    public async Task RunAsync_PathTwoPurgedRowsShare_IsHandledOnceAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        Purges(new PurgedProxy(Asset(1), Thumbnail), new PurgedProxy(Asset(2), Thumbnail));

        var result = await PurgeAsync(fileSystem, Asset(1), Asset(2));

        (result.Purged, result.ProxyFilesDeleted).Should().Be((2, 1));
        await store.Received(1).IsReferencedAsync(Thumbnail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ChecksEachPathOnlyAfterItsRowsWereDeletedAsync()
    {
        Purges(new PurgedProxy(Asset(1), Thumbnail));

        await PurgeAsync(new MockFileSystem(), Asset(1));

        Received.InOrder(() =>
        {
            _ = store.DeleteSoftDeletedAsync(ConfigId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
            _ = store.IsReferencedAsync(Thumbnail, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RunAsync_FileAlreadyGone_CountsAsDeletedAsync()
    {
        Purges(new PurgedProxy(Asset(1), Thumbnail));

        var result = await PurgeAsync(new MockFileSystem(), Asset(1));

        (result.ProxyFilesDeleted, result.ProxyFilesFailed).Should().Be((1, 0));
    }

    [Fact]
    public async Task RunAsync_DeleteFails_IsCountedAndTheRunGoesOnAsync()
    {
        var fileSystem = new DeleteFailingFileSystem($"/data/proxies/{Thumbnail}");
        File(fileSystem, Thumbnail);
        File(fileSystem, Preview);
        Purges(new PurgedProxy(Asset(1), Thumbnail), new PurgedProxy(Asset(1), Preview));

        var result = await PurgeAsync(fileSystem, Asset(1));

        (result.ProxyFilesDeleted, result.ProxyFilesFailed).Should().Be((1, 1));
        logger.Collector.GetSnapshot().Should().Contain(r =>
            r.Level == LogLevel.Warning && r.Message == $"Could not delete proxy file {Thumbnail}.");
    }

    [Fact]
    public async Task RunAsync_PathOutsideTheProxyRoot_IsFailedWithoutDeletingAsync()
    {
        const string escaping = "../originals/mira/a.jpg";
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/data/originals/mira/a.jpg", new MockFileData(new byte[10]));
        Purges(new PurgedProxy(Asset(1), escaping));

        var result = await PurgeAsync(fileSystem, Asset(1));

        fileSystem.File.Exists("/data/originals/mira/a.jpg").Should().BeTrue();
        result.ProxyFilesFailed.Should().Be(1);
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Warning)
            .Which.Message.Should().Be($"Did not delete proxy file {escaping} of a purged asset: it resolves outside the proxy root.");
        await store.DidNotReceive().IsReferencedAsync(escaping, Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Batches, progress and result
    // ==========================================================================

    [Fact]
    public async Task RunAsync_MoreIdsThanABatch_PurgesThemInBatchesReportingEachAsync()
    {
        var ids = Enumerable.Range(1, AssetPurgeHandler.BatchSize + 1).Select(Asset).ToArray();
        var (first, second) = (ids[..AssetPurgeHandler.BatchSize], ids[AssetPurgeHandler.BatchSize..]);

        var result = await PurgeAsync(new MockFileSystem(), ids);

        result.AlreadyGone.Should().Be(AssetPurgeHandler.BatchSize + 1);
        Received.InOrder(() =>
        {
            _ = store.DeleteSoftDeletedAsync(
                ConfigId, Arg.Is<IReadOnlyList<Guid>>(batch => batch.SequenceEqual(first)), Arg.Any<CancellationToken>());
            progress.Report(AssetPurgeHandler.BatchSize, AssetPurgeHandler.BatchSize + 1, "Purging soft-deleted assets");
            _ = store.DeleteSoftDeletedAsync(
                ConfigId, Arg.Is<IReadOnlyList<Guid>>(batch => batch.SequenceEqual(second)), Arg.Any<CancellationToken>());
            progress.Report(AssetPurgeHandler.BatchSize + 1, AssetPurgeHandler.BatchSize + 1, "Purging soft-deleted assets");
        });
    }

    [Fact]
    public async Task RunAsync_LogsTheCountsAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        Purges(new PurgedProxy(Asset(1), Thumbnail));
        Present(1);

        await PurgeAsync(fileSystem, Asset(1), Asset(2), Asset(3));

        logger.LatestRecord.Level.Should().Be(LogLevel.Information);
        logger.LatestRecord.Message.Should().Be(
            $"Purged soft-deleted assets of storage config {ConfigId}: 1 purged, 1 restored, 1 already gone; "
            + "proxy files: 1 deleted, 0 kept, 0 failed.");
    }
}
