using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Crawling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class MissingFileSweepTests
{
    private static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Kept = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Gone = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AlsoGone = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IMediaAssetRepository repository = Substitute.For<IMediaAssetRepository>();
    private readonly FakeLogger<MissingFileSweep> logger = new();

    private static UserStorageConfig Config => new() { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/data/originals/mira" };

    private static CrawlRun Walk(bool complete, params string[] found) => new(new CrawlResult(found.Length, found.Length, 0), 0)
    {
        FoundPaths = new HashSet<string>(found, StringComparer.Ordinal),
        WalkComplete = complete,
    };

    private void Stored(params AssetPath[] rows) => repository.GetActivePathsAsync(ConfigId, Arg.Any<CancellationToken>()).Returns(rows);

    private Task<MissingFiles> SweepAsync(CrawlRun run) => new MissingFileSweep(repository, logger).SweepAsync(Config, run, Ct);

    [Fact]
    public async Task SweepAsync_SoftDeletesExactlyTheActiveRowsTheWalkDidNotFindAsync()
    {
        Stored(new AssetPath(Kept, "2024/a.jpg"), new AssetPath(Gone, "2024/b.jpg"), new AssetPath(AlsoGone, "old/c.mov"));
        repository.SoftDeleteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(2);

        var result = await SweepAsync(Walk(complete: true, "2024/a.jpg", "2024/new.jpg"));

        result.Should().Be(new MissingFiles(SoftDeleted: 2, SkippedBecause: null));
        await repository.Received(1).SoftDeleteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { Gone, AlsoGone })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_ReportsTheRowsTheWriteSoftDeletedAsync()
    {
        Stored(new AssetPath(Gone, "2024/b.jpg"), new AssetPath(AlsoGone, "old/c.mov"));
        repository.SoftDeleteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(1);

        var result = await SweepAsync(Walk(complete: true, "2024/a.jpg"));

        result.SoftDeleted.Should().Be(1);
    }

    [Fact]
    public async Task SweepAsync_PathsDifferingOnlyInCase_AreNotTheSameFileAsync()
    {
        Stored(new AssetPath(Gone, "IMG_0001.JPG"));
        repository.SoftDeleteAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(1);

        await SweepAsync(Walk(complete: true, "img_0001.jpg"));

        await repository.Received(1).SoftDeleteAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { Gone })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SweepAsync_LogsEachSoftDeletedPathAtDebugAsync()
    {
        Stored(new AssetPath(Kept, "2024/a.jpg"), new AssetPath(Gone, "2024/b.jpg"));

        await SweepAsync(Walk(complete: true, "2024/a.jpg"));

        logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r =>
            r.Level == LogLevel.Debug
            && r.Message == "Soft-deleting asset 00000000-0000-0000-0000-000000000002 at 2024/b.jpg: a complete crawl did not find it.");
    }

    [Fact]
    public async Task SweepAsync_NothingMissing_WritesNothingAsync()
    {
        Stored(new AssetPath(Kept, "2024/a.jpg"));

        var result = await SweepAsync(Walk(complete: true, "2024/a.jpg"));

        result.Should().Be(new MissingFiles(0, null));
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default!, Ct);
    }

    [Fact]
    public async Task SweepAsync_IncompleteWalk_SkipsWithoutReadingOrWritingAsync()
    {
        Stored(new AssetPath(Gone, "2024/b.jpg"));

        var result = await SweepAsync(Walk(complete: false, "2024/a.jpg"));

        result.Should().Be(new MissingFiles(0, MissingFileSkip.IncompleteWalk));
        await repository.DidNotReceiveWithAnyArgs().GetActivePathsAsync(Guid.Empty, Ct);
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default!, Ct);
    }

    [Fact]
    public async Task SweepAsync_WalkFoundNothingWhileTheConfigHasActiveRows_SkipsAsEmptyRootAsync()
    {
        Stored(new AssetPath(Gone, "2024/b.jpg"));

        var result = await SweepAsync(Walk(complete: true));

        result.Should().Be(new MissingFiles(0, MissingFileSkip.EmptyRoot));
        await repository.DidNotReceiveWithAnyArgs().SoftDeleteAsync(default!, Ct);
    }

    [Fact]
    public async Task SweepAsync_EmptyRootAndNoActiveRows_IsNotASkipAsync()
    {
        Stored();

        var result = await SweepAsync(Walk(complete: true));

        result.Should().Be(new MissingFiles(0, null));
    }
}
