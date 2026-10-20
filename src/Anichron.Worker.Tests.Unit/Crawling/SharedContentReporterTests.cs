using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class SharedContentReporterTests
{
    private static readonly Guid Mira = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Home = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid Phone = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly Guid Backup = Guid.Parse("33333333-0000-0000-0000-000000000000");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public DuplicateCopyTally Tally { get; } = new();
        public FakeLogger<SharedContentReporter> Logger { get; } = new();
        public IReadOnlyCollection<Guid>? QueriedConfigs { get; private set; }

        private static UserStorageConfig Config(Guid id) => new() { Id = id, UserId = Mira, RootPath = "/unused" };

        public Task<DuplicateCounts> ReportAsync(
            Guid crawled, Guid[] usersConfigs, params ContentLocation[] locations)
        {
            Repository.FindSharedContentAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    QueriedConfigs = call.Arg<IReadOnlyCollection<Guid>>();
                    return locations;
                });

            return new SharedContentReporter(Repository, Tally, Logger)
                .ReportAsync(Config(crawled), [.. usersConfigs.Select(Config)], Ct);
        }

        public IEnumerable<FakeLogRecord> Pairs => Logger.Collector.GetSnapshot().Where(r => r.Level == LogLevel.Debug);

        public IEnumerable<string> Summaries
            => Logger.Collector.GetSnapshot().Where(r => r.Level == LogLevel.Information).Select(r => r.Message);
    }

    [Fact]
    public async Task ReportAsync_LooksAcrossTheCrawledConfigAndTheUsersOtherConfigsAsync()
    {
        var fx = new TestFixture();

        await fx.ReportAsync(Phone, [Home, Backup]);

        fx.QueriedConfigs.Should().Equal(Home, Backup, Phone);
    }

    [Fact]
    public async Task ReportAsync_CrawledConfigAlreadyAmongTheUsersConfigs_QueriesItOnceAsync()
    {
        var fx = new TestFixture();

        await fx.ReportAsync(Home, [Home, Phone]);

        fx.QueriedConfigs.Should().Equal(Home, Phone);
    }

    [Fact]
    public async Task ReportAsync_ContentInTwoConfigs_LogsThePairOnceAtDebugAsync()
    {
        var fx = new TestFixture();

        var counts = await fx.ReportAsync(
            Home, [Home, Phone],
            new ContentLocation(Mira, "cafe", Phone, "dcim/a.jpg"),
            new ContentLocation(Mira, "cafe", Home, "2023/a.jpg"));

        counts.Should().Be(new DuplicateCounts(0, 1));
        fx.Summaries.Should().ContainSingle().Which.Should().EndWith(
            "copies within it: 0, pairs with the user's other storage configs: 1.");
        fx.Pairs.Should().ContainSingle().Which.Message.Should().Be(
            "Duplicate content cafe for user aaaaaaaa-0000-0000-0000-000000000001: "
            + "2023/a.jpg in storage config 11111111-0000-0000-0000-000000000000 and "
            + "dcim/a.jpg in storage config 22222222-0000-0000-0000-000000000000.");
    }

    [Fact]
    public async Task ReportAsync_ContentInThreeConfigs_LogsOnlyThePairsInvolvingTheCrawledConfigAsync()
    {
        var fx = new TestFixture();

        var counts = await fx.ReportAsync(
            Phone, [Home, Phone, Backup],
            new ContentLocation(Mira, "cafe", Backup, "c.jpg"),
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"));

        counts.PairsAcross.Should().Be(2);
        fx.Pairs.Select(r => (r.GetStructuredStateValue("FilePath"), r.GetStructuredStateValue("OtherFilePath")))
            .Should().Equal(("a.jpg", "b.jpg"), ("b.jpg", "c.jpg"));
    }

    [Fact]
    public async Task ReportAsync_TwoHashes_PairsOnlyEqualContentAsync()
    {
        var fx = new TestFixture();

        await fx.ReportAsync(
            Home, [Home, Phone],
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "beef", Home, "x.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"),
            new ContentLocation(Mira, "beef", Phone, "y.jpg"));

        fx.Pairs.Select(r => (r.GetStructuredStateValue("ContentHash"), r.GetStructuredStateValue("FilePath")))
            .Should().BeEquivalentTo([("cafe", "a.jpg"), ("beef", "x.jpg")]);
    }

    [Fact]
    public async Task ReportAsync_CopiesWithinAndPairsAcross_SummarisesTheCrawlOnceAtInformationAsync()
    {
        var fx = new TestFixture();
        fx.Tally.Count(Home);
        fx.Tally.Count(Home);
        fx.Tally.Count(Phone);

        var counts = await fx.ReportAsync(
            Home, [Home, Phone],
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"));

        counts.Should().Be(new DuplicateCounts(2, 1));
        fx.Summaries.Should().Equal(
            "Duplicate content in storage config 11111111-0000-0000-0000-000000000000 of user "
            + "aaaaaaaa-0000-0000-0000-000000000001: copies within it: 2, pairs with the user's other storage configs: 1.");
    }

    [Fact]
    public async Task ReportAsync_OnlyCopiesWithin_StillSummarisesAsync()
    {
        var fx = new TestFixture();
        fx.Tally.Count(Home);

        (await fx.ReportAsync(Home, [Home])).Should().Be(new DuplicateCounts(1, 0));

        fx.Summaries.Should().ContainSingle().Which.Should().EndWith(
            "copies within it: 1, pairs with the user's other storage configs: 0.");
    }

    [Fact]
    public async Task ReportAsync_TakesOnlyTheCrawledConfigsCopiesAsync()
    {
        var fx = new TestFixture();
        fx.Tally.Count(Home);
        fx.Tally.Count(Phone);

        await fx.ReportAsync(Home, [Home, Phone]);

        (fx.Tally.Take(Home), fx.Tally.Take(Phone)).Should().Be((0, 1));
    }

    [Fact]
    public async Task ReportAsync_NothingDuplicated_LogsNothingAsync()
    {
        var fx = new TestFixture();

        (await fx.ReportAsync(Home, [Home, Phone])).Should().Be(new DuplicateCounts(0, 0));

        fx.Logger.Collector.GetSnapshot().Should().BeEmpty();
    }
}
