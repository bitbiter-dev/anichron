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
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Home = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid Phone = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly Guid Backup = Guid.Parse("33333333-0000-0000-0000-000000000000");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserStorageConfig Config(Guid id, Guid userId) => new() { Id = id, UserId = userId, RootPath = "/unused" };

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public DuplicateCopyTally Tally { get; } = new();
        public FakeLogger<SharedContentReporter> Logger { get; } = new();

        public async Task<IReadOnlyList<FakeLogRecord>> ReportAsync(
            UserStorageConfig[] crawled, params ContentLocation[] locations)
        {
            Repository.FindSharedContentAsync(
                    Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(crawled.Select(c => c.Id))),
                    Arg.Any<CancellationToken>())
                .Returns(locations);

            await new SharedContentReporter(Repository, Tally, Logger).ReportAsync(crawled, Ct);

            return Logger.Collector.GetSnapshot();
        }
    }

    private static IEnumerable<FakeLogRecord> Pairs(IEnumerable<FakeLogRecord> records)
        => records.Where(r => r.Level == LogLevel.Debug);

    private static IEnumerable<string> Summaries(IEnumerable<FakeLogRecord> records)
        => records.Where(r => r.Level == LogLevel.Information).Select(r => r.Message);

    // ==========================================================================
    // Pairs across configs
    // ==========================================================================

    [Fact]
    public async Task ReportAsync_ContentInTwoConfigs_LogsThePairOnceAtDebugAsync()
    {
        var records = await new TestFixture().ReportAsync(
            [Config(Home, Mira), Config(Phone, Mira)],
            new ContentLocation(Mira, "cafe", Phone, "dcim/a.jpg"),
            new ContentLocation(Mira, "cafe", Home, "2023/a.jpg"));

        Pairs(records).Should().ContainSingle().Which.Message.Should().Be(
            "Duplicate content cafe for user aaaaaaaa-0000-0000-0000-000000000001: "
            + "2023/a.jpg in storage config 11111111-0000-0000-0000-000000000000 and "
            + "dcim/a.jpg in storage config 22222222-0000-0000-0000-000000000000.");
    }

    [Fact]
    public async Task ReportAsync_ContentInThreeConfigs_LogsEachPairOnceAsync()
    {
        var records = await new TestFixture().ReportAsync(
            [Config(Home, Mira), Config(Phone, Mira), Config(Backup, Mira)],
            new ContentLocation(Mira, "cafe", Backup, "c.jpg"),
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"));

        Pairs(records).Select(r => (r.GetStructuredStateValue("FilePath"), r.GetStructuredStateValue("OtherFilePath")))
            .Should().Equal(("a.jpg", "b.jpg"), ("a.jpg", "c.jpg"), ("b.jpg", "c.jpg"));
    }

    [Fact]
    public async Task ReportAsync_TwoHashesAndTwoUsers_PairsOnlyWithinEachUsersHashAsync()
    {
        var records = await new TestFixture().ReportAsync(
            [Config(Home, Mira), Config(Phone, Mira), Config(Backup, Alice)],
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "beef", Home, "x.jpg"),
            new ContentLocation(Alice, "cafe", Backup, "theirs.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"),
            new ContentLocation(Alice, "cafe", Phone, "theirs-too.jpg"),
            new ContentLocation(Mira, "beef", Phone, "y.jpg"));

        Pairs(records).Select(r => (r.GetStructuredStateValue("UserId"), r.GetStructuredStateValue("ContentHash"),
                r.GetStructuredStateValue("FilePath"), r.GetStructuredStateValue("OtherFilePath")))
            .Should().BeEquivalentTo(
            [
                (Mira.ToString(), "cafe", "a.jpg", "b.jpg"),
                (Mira.ToString(), "beef", "x.jpg", "y.jpg"),
                (Alice.ToString(), "cafe", "theirs-too.jpg", "theirs.jpg"),
            ]);
    }

    // ==========================================================================
    // Summary per user
    // ==========================================================================

    [Fact]
    public async Task ReportAsync_CopiesWithinAndAcrossConfigs_SummarisesEachUserOnceAtInformationAsync()
    {
        var fx = new TestFixture();
        fx.Tally.Count(Home);
        fx.Tally.Count(Home);
        fx.Tally.Count(Phone);
        fx.Tally.Count(Backup);

        var records = await fx.ReportAsync(
            [Config(Home, Mira), Config(Phone, Mira), Config(Backup, Alice)],
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"));

        Summaries(records).Should().Equal(
            "Duplicate content for user aaaaaaaa-0000-0000-0000-000000000001: "
                + "copies within one storage config: 3, pairs across storage configs: 1.",
            "Duplicate content for user aaaaaaaa-0000-0000-0000-000000000002: "
                + "copies within one storage config: 1, pairs across storage configs: 0.");
    }

    [Fact]
    public async Task ReportAsync_OnlyAPairAcrossConfigs_StillSummarisesTheUserAsync()
    {
        var records = await new TestFixture().ReportAsync(
            [Config(Home, Mira), Config(Phone, Mira)],
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"));

        Summaries(records).Should().ContainSingle().Which.Should().EndWith(
            "copies within one storage config: 0, pairs across storage configs: 1.");
    }

    [Fact]
    public async Task ReportAsync_Reported_StartsTheNextCycleFromZeroAsync()
    {
        var fx = new TestFixture();
        fx.Tally.Count(Home);

        await fx.ReportAsync([Config(Home, Mira)]);

        fx.Tally.Take(Home).Should().Be(0);
    }

    [Fact]
    public async Task ReportAsync_NothingDuplicated_LogsNothingAsync()
        => (await new TestFixture().ReportAsync([Config(Home, Mira), Config(Backup, Alice)])).Should().BeEmpty();
}
