using Anichron.Core.Data.Repository;
using Anichron.Worker.Crawling;
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

    private static async Task<IReadOnlyList<FakeLogRecord>> ReportAsync(
        IReadOnlyCollection<Guid> crawled, params ContentLocation[] locations)
    {
        var repository = Substitute.For<IMediaAssetRepository>();
        repository.FindSharedContentAsync(crawled, Arg.Any<CancellationToken>()).Returns(locations);
        var logger = new FakeLogger<SharedContentReporter>();

        await new SharedContentReporter(repository, logger).ReportAsync(crawled, Ct);

        return logger.Collector.GetSnapshot();
    }

    [Fact]
    public async Task ReportAsync_ContentInTwoConfigs_LogsThePairOnceAtInformationAsync()
    {
        var records = await ReportAsync(
            [Home, Phone],
            new ContentLocation(Mira, "cafe", Phone, "dcim/a.jpg"),
            new ContentLocation(Mira, "cafe", Home, "2023/a.jpg"));

        records.Should().ContainSingle().Which.Should().Match<FakeLogRecord>(r => r.Level == LogLevel.Information
            && r.Message == "Duplicate content cafe for user aaaaaaaa-0000-0000-0000-000000000001: "
                + "2023/a.jpg in storage config 11111111-0000-0000-0000-000000000000 and "
                + "dcim/a.jpg in storage config 22222222-0000-0000-0000-000000000000.");
    }

    [Fact]
    public async Task ReportAsync_ContentInThreeConfigs_LogsEachPairOnceAsync()
    {
        var records = await ReportAsync(
            [Home, Phone, Backup],
            new ContentLocation(Mira, "cafe", Backup, "c.jpg"),
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"));

        records.Select(r => (r.GetStructuredStateValue("FilePath"), r.GetStructuredStateValue("OtherFilePath")))
            .Should().Equal(("a.jpg", "b.jpg"), ("a.jpg", "c.jpg"), ("b.jpg", "c.jpg"));
    }

    [Fact]
    public async Task ReportAsync_TwoHashesAndTwoUsers_PairsOnlyWithinEachUsersHashAsync()
    {
        var records = await ReportAsync(
            [Home, Phone, Backup],
            new ContentLocation(Mira, "cafe", Home, "a.jpg"),
            new ContentLocation(Mira, "beef", Home, "x.jpg"),
            new ContentLocation(Alice, "cafe", Backup, "theirs.jpg"),
            new ContentLocation(Mira, "cafe", Phone, "b.jpg"),
            new ContentLocation(Alice, "cafe", Phone, "theirs-too.jpg"),
            new ContentLocation(Mira, "beef", Phone, "y.jpg"));

        records.Select(r => (r.GetStructuredStateValue("UserId"), r.GetStructuredStateValue("ContentHash"),
                r.GetStructuredStateValue("FilePath"), r.GetStructuredStateValue("OtherFilePath")))
            .Should().BeEquivalentTo(
            [
                (Mira.ToString(), "cafe", "a.jpg", "b.jpg"),
                (Mira.ToString(), "beef", "x.jpg", "y.jpg"),
                (Alice.ToString(), "cafe", "theirs-too.jpg", "theirs.jpg"),
            ]);
    }

    [Fact]
    public async Task ReportAsync_NothingShared_LogsNothingAsync()
        => (await ReportAsync([Home])).Should().BeEmpty();
}
