using Anichron.Core.Work;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.ProxySweeps;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.ProxySweeps;

public sealed class ProxySweepTests
{
    private const string LiveDirectory = "ab/c1000000000000000000000000000000-ab01";
    private const string GoneDirectory = "cd/c9000000000000000000000000000000-cd01";
    private static readonly Guid LiveConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid GoneConfigId = Guid.Parse("c9000000-0000-0000-0000-000000000000");
    private static readonly Guid SweepId = Guid.Parse("50000000-0000-0000-0000-000000000000");
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 27, 12, 0);
    private static readonly Instant TwoHoursAgo = Now - Duration.FromHours(2);

    private readonly MockFileSystem fileSystem = new();
    private readonly IProxyOrphanStore store = Substitute.For<IProxyOrphanStore>();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly FakeLogger<ProxySweepHandler> logger = new();
    private readonly List<FoundOrphan> recorded = [];

    public ProxySweepTests()
    {
        clock.GetCurrentInstant().Returns(Now);
        fileSystem.AddDirectory("/data/proxies");
        store.ReferencedUnderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        store.ExistingConfigsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().Where(id => id != GoneConfigId).ToHashSet());
        store.When(s => s.RecordAsync(SweepId, Arg.Any<IReadOnlyList<FoundOrphan>>(), Arg.Any<CancellationToken>()))
            .Do(call => recorded.AddRange(call.Arg<IReadOnlyList<FoundOrphan>>()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void File(string relativePath, Instant lastWrite, int size = 10)
        => fileSystem.AddFile($"/data/proxies/{relativePath}", new MockFileData(new byte[size])
        {
            LastWriteTime = lastWrite.ToDateTimeOffset(),
        });

    private void Referenced(string shard, params string[] paths)
        => store.ReferencedUnderAsync(shard, Arg.Any<CancellationToken>()).Returns(paths.ToHashSet());

    private async Task<ProxySweepResult> RunAsync()
    {
        var result = await new ProxySweepHandler(store, new TwoLevelHexShardStrategy(), fileSystem, clock, logger)
            .RunAsync(new ProxySweepPayload(), new WorkRun(SweepId, progress), Ct);
        return result.Should().BeOfType<ProxySweepResult>().Subject;
    }

    // ==========================================================================
    // The orphan rule
    // ==========================================================================

    [Fact]
    public async Task RunAsync_UnreferencedOldFile_IsRecordedAsItWasAtTheScanAsync()
    {
        File($"{LiveDirectory}/thumbnail.jpg", Instant.FromUtc(2026, 10, 27, 9, 30, 15) + Duration.FromTicks(12_345_678), size: 300);

        await RunAsync();

        recorded.Should().Equal(new FoundOrphan(
            $"{LiveDirectory}/thumbnail.jpg", LiveConfigId, 300,
            Instant.FromUtc(2026, 10, 27, 9, 30, 15) + Duration.FromTicks(12_345_670)));
    }

    [Fact]
    public async Task RunAsync_FileAProxyRowReferences_IsNotRecordedAsync()
    {
        File($"{LiveDirectory}/thumbnail.jpg", TwoHoursAgo);
        File($"{LiveDirectory}/preview.jpg", TwoHoursAgo);
        Referenced("ab", $"{LiveDirectory}/thumbnail.jpg");

        await RunAsync();

        recorded.Select(o => o.ProxyPath).Should().Equal($"{LiveDirectory}/preview.jpg");
    }

    [Fact]
    public async Task RunAsync_ReferenceInAnotherShard_DoesNotCoverTheFileAsync()
    {
        File($"{LiveDirectory}/thumbnail.jpg", TwoHoursAgo);
        Referenced("cd", $"{LiveDirectory}/thumbnail.jpg");

        await RunAsync();

        recorded.Select(o => o.ProxyPath).Should().Equal($"{LiveDirectory}/thumbnail.jpg");
    }

    [Fact]
    public async Task RunAsync_FileWrittenWithinTheGracePeriod_IsNotRecordedAsync()
    {
        File($"{LiveDirectory}/thumbnail.jpg", Now - Duration.FromHours(1));
        File($"{LiveDirectory}/preview.jpg", Now - Duration.FromMinutes(59));
        File($"{LiveDirectory}/video.mp4", Now - Duration.FromHours(1) - Duration.FromTicks(10));

        await RunAsync();

        recorded.Select(o => o.ProxyPath).Should().Equal($"{LiveDirectory}/video.mp4");
    }

    [Fact]
    public async Task RunAsync_FileDeeperInsideAShard_IsRecordedAsync()
    {
        File($"{LiveDirectory}/nested/stray.bin", TwoHoursAgo);

        await RunAsync();

        recorded.Should().ContainSingle().Which.Should().Be(
            new FoundOrphan($"{LiveDirectory}/nested/stray.bin", LiveConfigId, 10, TwoHoursAgo));
    }

    [Fact]
    public async Task RunAsync_FileInAFolderNamingNoConfig_IsRecordedWithNoneAsync()
    {
        File("ab/lost/stray.bin", TwoHoursAgo);

        await RunAsync();

        recorded.Should().ContainSingle().Which.StorageConfigId.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_UnreferencedFileAtTheRoot_IsRecordedAsync()
    {
        File("stray.txt", TwoHoursAgo);

        await RunAsync();

        recorded.Should().Equal(new FoundOrphan("stray.txt", null, 10, TwoHoursAgo));
    }

    [Fact]
    public async Task RunAsync_ReferencedFileAtTheRoot_IsNotRecordedAsync()
    {
        File("stray.txt", TwoHoursAgo);
        store.IsReferencedAsync("stray.txt", Arg.Any<CancellationToken>()).Returns(true);

        await RunAsync();

        recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_YoungFileAtTheRoot_IsNotRecordedAsync()
    {
        File("stray.txt", Now - Duration.FromMinutes(5));

        await RunAsync();

        recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MissingProxyRoot_FailsAndRecordsNothingAsync()
    {
        fileSystem.Directory.Delete("/data/proxies");

        var act = RunAsync;

        await act.Should().ThrowAsync<DirectoryNotFoundException>().WithMessage("The proxy root /data/proxies is missing.");
        await store.DidNotReceiveWithAnyArgs().RecordAsync(Guid.Empty, default!, Ct);
    }

    // ==========================================================================
    // Recording, progress and result
    // ==========================================================================

    [Fact]
    public async Task RunAsync_RecordsEachShardOnItsOwnAsync()
    {
        File($"{LiveDirectory}/thumbnail.jpg", TwoHoursAgo);
        File($"{GoneDirectory}/thumbnail.jpg", TwoHoursAgo);

        await RunAsync();

        Received.InOrder(() =>
        {
            _ = store.RecordAsync(SweepId, Arg.Is<IReadOnlyList<FoundOrphan>>(o => o.Count == 0), Arg.Any<CancellationToken>());
            _ = store.RecordAsync(SweepId, Arg.Is<IReadOnlyList<FoundOrphan>>(o => o.Single().ProxyPath == $"{LiveDirectory}/thumbnail.jpg"), Arg.Any<CancellationToken>());
            _ = store.RecordAsync(SweepId, Arg.Is<IReadOnlyList<FoundOrphan>>(o => o.Single().ProxyPath == $"{GoneDirectory}/thumbnail.jpg"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RunAsync_ReportsShardsDoneOutOfTheShardsFoundAsync()
    {
        File($"{LiveDirectory}/thumbnail.jpg", TwoHoursAgo);
        File($"{GoneDirectory}/thumbnail.jpg", TwoHoursAgo);

        await RunAsync();

        Received.InOrder(() =>
        {
            progress.Report(0, 2, "Scanning proxy shards");
            progress.Report(1, 2, "Scanning proxy shards");
            progress.Report(2, 2, "Scanning proxy shards");
        });
    }

    [Fact]
    public async Task RunAsync_SummarisesEveryOrphanFoundAsync()
    {
        File("stray.txt", TwoHoursAgo, size: 1);
        File($"{LiveDirectory}/thumbnail.jpg", TwoHoursAgo, size: 20);
        File($"{LiveDirectory}/preview.jpg.0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f.tmp", TwoHoursAgo, size: 300);
        File($"{GoneDirectory}/thumbnail.jpg", TwoHoursAgo, size: 4000);
        File($"{GoneDirectory}/preview.jpg", TwoHoursAgo, size: 50000);
        File("cd/c8000000000000000000000000000000-cd02/thumbnail.tmp.jpg", TwoHoursAgo, size: 600000);

        var result = await RunAsync();

        result.Should().Be(new ProxySweepResult(Orphans: 6, Bytes: 654321, TemporaryFiles: 1, ConfigsGone: 1));
    }

    [Fact]
    public async Task RunAsync_LogsTheSummaryAsync()
    {
        File($"{GoneDirectory}/thumbnail.jpg", TwoHoursAgo, size: 7);

        await RunAsync();

        logger.LatestRecord.Message.Should().Be(
            "Swept the proxy root: 1 orphan(s), 7 B, 0 temporary file(s), 1 storage config(s) gone.");
    }
}
