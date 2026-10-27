using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.ProxySweeps;
using Anichron.Worker.Tests.Unit.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.ProxySweeps;

public sealed class ProxyDeletionTests
{
    private const string Shard = "/data/proxies/ab";
    private const string ContentDirectory = "/data/proxies/ab/c1000000000000000000000000000000-ab01";
    private const string Thumbnail = "ab/c1000000000000000000000000000000-ab01/thumbnail.jpg";
    private const string Preview = "ab/c1000000000000000000000000000000-ab01/preview.jpg";
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Guid ConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Instant Scanned = Instant.FromUtc(2026, 10, 27, 9, 30, 15);
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 27, 12, 0);

    private readonly IProxyOrphanStore store = Substitute.For<IProxyOrphanStore>();
    private readonly IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly FakeLogger<ProxyDeletionHandler> logger = new();
    private readonly ProxyDeletionPayload payload = new(SweepId, ConfigId, null);

    public ProxyDeletionTests() => clock.GetCurrentInstant().Returns(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void File(MockFileSystem fileSystem, string relativePath, int size = 10, Instant? lastWrite = null)
        => fileSystem.AddFile($"/data/proxies/{relativePath}", new MockFileData(new byte[size])
        {
            LastWriteTime = (lastWrite ?? Scanned).ToDateTimeOffset(),
        });

    private static ProxyOrphan Orphan(string relativePath, int size = 10)
        => new()
        {
            Id = Guid.NewGuid(),
            SweepId = SweepId,
            ProxyPath = relativePath,
            StorageConfigId = ConfigId,
            SizeBytes = size,
            LastWriteTime = Scanned,
        };

    private void Pending(params ProxyOrphan[][] batches)
        => store.GetPendingAsync(payload, ProxyDeletionHandler.BatchSize, Arg.Any<CancellationToken>())
            .Returns(batches[0], [.. batches.Skip(1).Append([])]);

    private async Task<ProxyDeletionResult> RunAsync(IFileSystem fileSystem)
    {
        var result = await new ProxyDeletionHandler(store, unitOfWork, new TwoLevelHexShardStrategy(), fileSystem, clock, logger)
            .RunAsync(payload, new WorkRun(Guid.NewGuid(), progress), Ct);
        return result.Should().BeOfType<ProxyDeletionResult>().Subject;
    }

    // ==========================================================================
    // Re-check before delete
    // ==========================================================================

    [Fact]
    public async Task RunAsync_UnchangedUnreferencedFile_IsDeletedAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);

        var result = await RunAsync(fileSystem);

        fileSystem.File.Exists($"/data/proxies/{Thumbnail}").Should().BeFalse();
        (orphan.DeletionOutcome, orphan.ProcessedAt).Should().Be((ProxyDeletionOutcome.Deleted, Now));
        result.Should().Be(new ProxyDeletionResult(Deleted: 1, SkippedReferenced: 0, SkippedChanged: 0, Missing: 0, Failed: 0));
    }

    [Fact]
    public async Task RunAsync_MtimeDifferingBelowAMicrosecond_IsUnchangedAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail, lastWrite: Scanned + Duration.FromTicks(9));
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);

        await RunAsync(fileSystem);

        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.Deleted);
    }

    [Fact]
    public async Task RunAsync_FileARowReferencesAgain_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);
        store.IsReferencedAsync(Thumbnail, Arg.Any<CancellationToken>()).Returns(true);

        var result = await RunAsync(fileSystem);

        fileSystem.File.Exists($"/data/proxies/{Thumbnail}").Should().BeTrue();
        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.SkippedReferenced);
        result.Should().Be(new ProxyDeletionResult(Deleted: 0, SkippedReferenced: 1, SkippedChanged: 0, Missing: 0, Failed: 0));
    }

    [Fact]
    public async Task RunAsync_FileWithAnotherSize_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail, size: 11);
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);

        var result = await RunAsync(fileSystem);

        fileSystem.File.Exists($"/data/proxies/{Thumbnail}").Should().BeTrue();
        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.SkippedChanged);
        result.Should().Be(new ProxyDeletionResult(Deleted: 0, SkippedReferenced: 0, SkippedChanged: 1, Missing: 0, Failed: 0));
    }

    [Fact]
    public async Task RunAsync_FileWrittenAgain_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail, lastWrite: Scanned + Duration.FromTicks(10));
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);

        await RunAsync(fileSystem);

        fileSystem.File.Exists($"/data/proxies/{Thumbnail}").Should().BeTrue();
        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.SkippedChanged);
    }

    [Fact]
    public async Task RunAsync_FileGone_IsMissingAsync()
    {
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);

        var result = await RunAsync(new MockFileSystem());

        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.Missing);
        result.Should().Be(new ProxyDeletionResult(Deleted: 0, SkippedReferenced: 0, SkippedChanged: 0, Missing: 1, Failed: 0));
    }

    [Fact]
    public async Task RunAsync_DeleteFails_IsFailedAndTheRunGoesOnAsync()
    {
        var fileSystem = new DeleteFailingFileSystem($"/data/proxies/{Thumbnail}");
        File(fileSystem, Thumbnail);
        File(fileSystem, Preview);
        var failing = Orphan(Thumbnail);
        var next = Orphan(Preview);
        Pending([failing, next]);

        var result = await RunAsync(fileSystem);

        (failing.DeletionOutcome, next.DeletionOutcome).Should().Be((ProxyDeletionOutcome.Failed, ProxyDeletionOutcome.Deleted));
        result.Should().Be(new ProxyDeletionResult(Deleted: 1, SkippedReferenced: 0, SkippedChanged: 0, Missing: 0, Failed: 1));
        logger.Collector.GetSnapshot().Should().Contain(r =>
            r.Level == LogLevel.Warning && r.Message == $"Could not delete orphaned proxy file {Thumbnail}.");
    }

    // ==========================================================================
    // Directories
    // ==========================================================================

    [Fact]
    public async Task RunAsync_LastFileOfAContentDirectory_RemovesItButKeepsTheShardAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        Pending([Orphan(Thumbnail)]);

        await RunAsync(fileSystem);

        fileSystem.Directory.Exists(ContentDirectory).Should().BeFalse();
        fileSystem.Directory.Exists(Shard).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ContentDirectoryStillHoldingAFile_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        File(fileSystem, Preview);
        Pending([Orphan(Thumbnail)]);

        await RunAsync(fileSystem);

        fileSystem.Directory.Exists(ContentDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ContentDirectoryStillHoldingAFolder_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        fileSystem.AddDirectory($"{ContentDirectory}/nested");
        Pending([Orphan(Thumbnail)]);

        await RunAsync(fileSystem);

        fileSystem.Directory.Exists($"{ContentDirectory}/nested").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_FileDeeperThanAContentDirectory_RemovesNoFolderAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, "ab/c1000000000000000000000000000000-ab01/nested/stray.bin");
        Pending([Orphan("ab/c1000000000000000000000000000000-ab01/nested/stray.bin")]);

        await RunAsync(fileSystem);

        fileSystem.Directory.Exists($"{ContentDirectory}/nested").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_FolderNamingNoConfig_IsKeptAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, "ab/lost/stray.bin");
        Pending([Orphan("ab/lost/stray.bin")]);

        await RunAsync(fileSystem);

        fileSystem.Directory.Exists("/data/proxies/ab/lost").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_FileDirectlyInAShard_KeepsTheShardAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, "ab/stray.bin");
        Pending([Orphan("ab/stray.bin")]);

        await RunAsync(fileSystem);

        fileSystem.Directory.Exists(Shard).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_FileAtTheRoot_KeepsTheRootAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, "stray.txt");
        var orphan = Orphan("stray.txt");
        Pending([orphan]);

        await RunAsync(fileSystem);

        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.Deleted);
        fileSystem.Directory.Exists("/data/proxies").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ContentDirectoryThatCannotBeRemoved_KeepsTheFileDeletedAsync()
    {
        var fileSystem = new DirectoryDeleteFailingFileSystem();
        File(fileSystem, Thumbnail);
        var orphan = Orphan(Thumbnail);
        Pending([orphan]);

        await RunAsync(fileSystem);

        orphan.DeletionOutcome.Should().Be(ProxyDeletionOutcome.Deleted);
        fileSystem.Directory.Exists(ContentDirectory).Should().BeTrue();
    }

    // ==========================================================================
    // Batches, saves, progress and result
    // ==========================================================================

    [Fact]
    public async Task RunAsync_SavesEachOutcomeOnceItIsSetAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        var first = Orphan(Thumbnail);
        var second = Orphan(Preview);
        Pending([first, second]);
        var saved = new List<(ProxyDeletionOutcome?, ProxyDeletionOutcome?)>();
        unitOfWork.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => saved.Add((first.DeletionOutcome, second.DeletionOutcome)));

        await RunAsync(fileSystem);

        saved.Should().Equal(
            (ProxyDeletionOutcome.Deleted, null),
            (ProxyDeletionOutcome.Deleted, ProxyDeletionOutcome.Missing));
    }

    [Fact]
    public async Task RunAsync_ReadsBatchesUntilNoneIsPendingForgettingEachAsync()
    {
        Pending([Orphan(Thumbnail)], [Orphan(Preview)]);

        var result = await RunAsync(new MockFileSystem());

        result.Missing.Should().Be(2);
        Received.InOrder(() =>
        {
            _ = store.GetPendingAsync(payload, ProxyDeletionHandler.BatchSize, Arg.Any<CancellationToken>());
            store.ForgetLoaded();
            _ = store.GetPendingAsync(payload, ProxyDeletionHandler.BatchSize, Arg.Any<CancellationToken>());
            store.ForgetLoaded();
            _ = store.GetPendingAsync(payload, ProxyDeletionHandler.BatchSize, Arg.Any<CancellationToken>());
        });
        store.Received(2).ForgetLoaded();
    }

    [Fact]
    public async Task RunAsync_ReportsOrphansProcessedAsync()
    {
        Pending([Orphan(Thumbnail), Orphan(Preview)]);

        await RunAsync(new MockFileSystem());

        Received.InOrder(() =>
        {
            progress.Report(1, null, "Deleting orphaned proxy files");
            progress.Report(2, null, "Deleting orphaned proxy files");
        });
    }

    [Fact]
    public async Task RunAsync_NothingPending_DoesNothingAsync()
    {
        store.GetPendingAsync(payload, ProxyDeletionHandler.BatchSize, Arg.Any<CancellationToken>()).Returns([]);

        var result = await RunAsync(new MockFileSystem());

        result.Should().Be(new ProxyDeletionResult(Deleted: 0, SkippedReferenced: 0, SkippedChanged: 0, Missing: 0, Failed: 0));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_LogsTheCountsAsync()
    {
        var fileSystem = new MockFileSystem();
        File(fileSystem, Thumbnail);
        Pending([Orphan(Thumbnail), Orphan(Preview)]);

        await RunAsync(fileSystem);

        logger.LatestRecord.Message.Should().Be(
            $"Deleted orphaned proxy files of sweep {SweepId}: 1 deleted, 0 referenced, 0 changed, 1 missing, 0 failed.");
    }

    [Fact]
    public void Counting_NoDeletionOutcome_Throws()
    {
        var act = () => ProxyDeletionResult.None.Counting((ProxyDeletionOutcome)99);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("Not a deletion outcome.*");
    }

    private sealed class DirectoryDeleteFailingFileSystem : MockFileSystem
    {
        // MockFileSystem's constructor reads Directory before this class's constructor runs.
        public override IDirectory Directory => field ??= new FailingDirectory(this);

        private sealed class FailingDirectory(MockFileSystem fileSystem) : MockDirectory(fileSystem, "/")
        {
            public override void Delete(string path) => throw new IOException($"Simulated delete failure for {path}.");
        }
    }
}
