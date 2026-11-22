using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.Data;
using Anichron.Worker.CompactPreviews;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.CompactPreviews;

public sealed class CompactPreviewTests : IAsyncDisposable
{
    private static readonly Guid MiraId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ConfigId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 22, 9, 0);
    private static readonly byte[] Encoded = [9, 8, 7];

    private readonly string databaseName = Guid.NewGuid().ToString();
    private readonly AnichronDbContext db;
    private readonly MockFileSystem fileSystem = new();
    private readonly ICompactPreviewEncoder encoder = Substitute.For<ICompactPreviewEncoder>();
    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly IGuidFactory guids = Substitute.For<IGuidFactory>();
    private readonly IWorkProgress progress = Substitute.For<IWorkProgress>();
    private readonly FakeLogger<CompactPreviewHandler> logger = new();
    private readonly FakeLogger<ActiveStorageConfigs> activeLogger = new();
    private int nextGuid;

    public CompactPreviewTests()
    {
        db = AnichronDbContexts.InMemory(databaseName);
        clock.GetCurrentInstant().Returns(Now);
        guids.NewGuid().Returns(_ => Guid.Parse($"bbbbbbbb-0000-0000-0000-{++nextGuid:D12}"));
        encoder.Encode(Arg.Any<byte[]>()).Returns(Encoded);
        Seed(new User { Id = MiraId, Username = "mira", Email = "mira@example.com", PasswordHash = "h" });
        Seed(new UserStorageConfig { Id = ConfigId, UserId = MiraId, RootPath = "/data/originals/mira", IsActive = true });
    }

    public async ValueTask DisposeAsync() => await db.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Guid IdOf(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static string DirectoryOf(string hash) => $"{hash[..2]}/{ConfigId:N}-{hash}";

    private void Seed(params object[] entities)
    {
        using var seed = AnichronDbContexts.InMemory(databaseName);
        seed.AddRange(entities);
        seed.SaveChanges();
    }

    private MediaAsset Pending(int n, string? previewPath = null)
    {
        var id = IdOf(n);
        var hash = $"ab{n:D14}";
        var path = previewPath ?? $"{DirectoryOf(hash)}/preview.jpg";
        var asset = new MediaAsset
        {
            Id = id,
            StorageConfigId = ConfigId,
            FilePath = $"{n}.jpg",
            FileName = $"{n}.jpg",
            ContentHash = hash,
            ProxyFiles = [new ProxyFile { Id = Guid.NewGuid(), AssetId = id, ProxyType = ProxyType.FullPreview, ProxyPath = path }],
        };
        Seed(asset);
        if (StoragePath.ResolveProxy(path) is { } absolute)
            fileSystem.AddFile(absolute, new MockFileData([(byte)n]));
        return asset;
    }

    private static CompactPreviewPayload Chunk(Guid? after = null, Guid? configId = null)
        => new(configId ?? ConfigId, after);

    private Task<object?> RunAsync(CompactPreviewPayload payload, CancellationToken ct, int chunkSize = 10)
        => new CompactPreviewHandler(
                new ActiveStorageConfigs(new EfUserStorageConfigRepository(db), activeLogger),
                new EfMediaAssetRepository(db),
                db,
                queue,
                new TwoLevelHexShardStrategy(),
                new ProxyStagingWriter(fileSystem, clock, guids, new FakeLogger<ProxyStagingWriter>()),
                encoder,
                fileSystem,
                guids,
                Options.Create(new WorkerSettings { CompactPreviewChunkSize = chunkSize }),
                logger)
            .RunAsync(payload, new WorkRun(Guid.NewGuid(), progress), ct);

    private List<ProxyFile> StoredCompactPreviews()
    {
        using var read = AnichronDbContexts.InMemory(databaseName);
        return [.. read.ProxyFiles.IgnoreQueryFilters().Where(p => p.ProxyType == ProxyType.CompactPreview).OrderBy(p => p.AssetId)];
    }

    private string[] TemporaryFiles()
        => [.. fileSystem.AllFiles.Where(f => f.EndsWith(ProxyStagingWriter.TemporaryFileSuffix, StringComparison.Ordinal))];

    // ==========================================================================
    // Skipping
    // ==========================================================================

    [Fact]
    public async Task RunAsync_InactiveConfig_SkipsWithoutEncodingAsync()
    {
        var inactive = Guid.NewGuid();
        Seed(new UserStorageConfig { Id = inactive, UserId = MiraId, RootPath = "/data/originals/off", IsActive = false });

        var result = await RunAsync(Chunk(configId: inactive), Ct);

        result.Should().Be(StorageConfigSkipped.MissingOrInactive);
        encoder.DidNotReceiveWithAnyArgs().Encode(default!);
        activeLogger.Collector.GetSnapshot().Should().ContainSingle().Which.Message.Should().Be(
            $"Skipping CompactPreview of storage config {inactive}: it is missing or inactive.");
    }

    // ==========================================================================
    // Converting
    // ==========================================================================

    [Fact]
    public async Task RunAsync_PendingAsset_WritesItsCompactPreviewBesideThePreviewAndRecordsItAsync()
    {
        var asset = Pending(1);

        var result = await RunAsync(Chunk(), Ct);

        result.Should().Be(new CompactPreviewResult { Converted = 1, Failed = 0, Remaining = 0 });
        encoder.Received(1).Encode(Arg.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 1 })));
        var path = $"{DirectoryOf(asset.ContentHash)}/compact-preview.jxl";
        (await fileSystem.File.ReadAllBytesAsync($"/data/proxies/{path}", Ct)).Should().Equal(Encoded);
        StoredCompactPreviews().Should().ContainSingle().Which.Should().Match<ProxyFile>(p =>
            p.AssetId == asset.Id && p.ProxyPath == path && p.SizeBytes == Encoded.Length && p.CreatedAt == Now);
        TemporaryFiles().Should().BeEmpty();
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<CompactPreviewPayload>(default!, default, Ct);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressAndLogsTheTallyAsync()
    {
        Pending(1);
        Pending(2);

        await RunAsync(Chunk(), Ct);

        progress.Received(1).Report(1, 2, "Making compact previews");
        progress.Received(1).Report(2, 2, "Making compact previews");
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Information).Which.Message.Should().Be(
            $"Compact previews for storage config {ConfigId}: 2 made, 0 failed, 0 remaining.");
    }

    [Fact]
    public async Task RunAsync_MorePendingThanAChunk_ConvertsTheFirstByIdAndEnqueuesTheNextChunkAsync()
    {
        Pending(3);
        Pending(1);
        Pending(2);

        var result = await RunAsync(Chunk(), Ct, chunkSize: 2);

        result.Should().Be(new CompactPreviewResult { Converted = 2, Failed = 0, Remaining = 1 });
        StoredCompactPreviews().Select(p => p.AssetId).Should().Equal(IdOf(1), IdOf(2));
        await queue.Received(1).EnqueueAsync(new CompactPreviewPayload(ConfigId, IdOf(2)), null, Ct);
    }

    [Fact]
    public async Task RunAsync_FromACursor_ConvertsOnlyWhatComesAfterItAsync()
    {
        Pending(1);
        Pending(2);

        var result = await RunAsync(Chunk(after: IdOf(1)), Ct);

        result.Should().Be(new CompactPreviewResult { Converted = 1, Failed = 0, Remaining = 0 });
        StoredCompactPreviews().Should().ContainSingle().Which.AssetId.Should().Be(IdOf(2));
    }

    [Fact]
    public async Task RunAsync_NothingAfterTheCursor_EnqueuesNothingAsync()
    {
        Pending(1);

        var result = await RunAsync(Chunk(after: IdOf(1)), Ct);

        result.Should().Be(new CompactPreviewResult { Converted = 0, Failed = 0, Remaining = 0 });
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync<CompactPreviewPayload>(default!, default, Ct);
    }

    // ==========================================================================
    // Failing
    // ==========================================================================

    [Fact]
    public async Task RunAsync_EncodeFails_LogsCountsAndSkipsItWithoutDebrisAsync()
    {
        Pending(1);
        Pending(2);
        encoder.Encode(Arg.Is<byte[]>(bytes => bytes[0] == 1)).Returns(_ => throw new InvalidOperationException("bad pixels"));

        var result = await RunAsync(Chunk(), Ct);

        result.Should().Be(new CompactPreviewResult { Converted = 1, Failed = 1, Remaining = 0 });
        StoredCompactPreviews().Should().ContainSingle().Which.AssetId.Should().Be(IdOf(2));
        TemporaryFiles().Should().BeEmpty();
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning).Which.Should().Match<FakeLogRecord>(e =>
            e.Message == $"Making the compact preview of asset {IdOf(1)} failed." && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task RunAsync_AChunkThatOnlyFailed_MovesItsCursorPastTheFailuresAsync()
    {
        Pending(1);
        Pending(2);
        encoder.Encode(Arg.Is<byte[]>(bytes => bytes[0] == 1)).Returns(_ => throw new InvalidOperationException("bad pixels"));

        var result = await RunAsync(Chunk(), Ct, chunkSize: 1);

        result.Should().Be(new CompactPreviewResult { Converted = 0, Failed = 1, Remaining = 1 });
        await queue.Received(1).EnqueueAsync(new CompactPreviewPayload(ConfigId, IdOf(1)), null, Ct);
    }

    [Fact]
    public async Task RunAsync_PreviewFileMissing_CountsItAsFailedAsync()
    {
        Pending(1);
        fileSystem.File.Delete($"/data/proxies/{DirectoryOf(Pending(2).ContentHash)}/preview.jpg");

        var result = await RunAsync(Chunk(), Ct);

        result.Should().Be(new CompactPreviewResult { Converted = 1, Failed = 1, Remaining = 0 });
    }

    [Fact]
    public async Task RunAsync_PreviewPathOutsideTheProxyRoot_CountsItAsFailedWithoutReadingAsync()
    {
        Pending(1, previewPath: "../../etc/passwd");

        var result = await RunAsync(Chunk(), Ct);

        result.Should().Be(new CompactPreviewResult { Converted = 0, Failed = 1, Remaining = 0 });
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_Cancelled_StopsInsteadOfCountingAFailureAsync()
    {
        Pending(1);
        using var cancel = new CancellationTokenSource();
        encoder.Encode(Arg.Any<byte[]>()).Returns(_ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });

        var act = () => RunAsync(Chunk(), cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        StoredCompactPreviews().Should().BeEmpty();
    }

    [Fact]
    public void Result_NamesItsCountsInItsJson()
        => WorkJson.SerializeResult(new CompactPreviewResult { Converted = 1, Failed = 2, Remaining = 3 })
            .Should().Be("""{"converted":1,"failed":2,"remaining":3}""");
}
