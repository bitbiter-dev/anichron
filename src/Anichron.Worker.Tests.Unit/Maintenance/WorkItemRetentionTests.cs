using Anichron.Core.Work;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Maintenance;

public sealed class WorkItemRetentionTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 18, 12, 0);

    private readonly IWorkItemStore store = Substitute.For<IWorkItemStore>();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly FakeLogger<WorkItemRetentionHandler> logger = new();

    public WorkItemRetentionTests() => clock.GetCurrentInstant().Returns(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<object?> RunAsync(WorkerSettings? settings = null)
        => new WorkItemRetentionHandler(store, clock, Options.Create(settings ?? new WorkerSettings()), logger)
            .RunAsync(new WorkItemRetentionPayload(), Substitute.For<IWorkProgress>(), Ct);

    [Fact]
    public async Task RunAsync_ByDefault_DeletesItemsFinishedMoreThanThirtyDaysAgoAsync()
    {
        await RunAsync();

        await store.Received(1).DeleteFinishedBeforeAsync(Instant.FromUtc(2026, 9, 18, 12, 0), Ct);
    }

    [Fact]
    public async Task RunAsync_UsesTheConfiguredRetentionAsync()
    {
        await RunAsync(new WorkerSettings { WorkItemRetentionDays = 1.5 });

        await store.Received(1).DeleteFinishedBeforeAsync(Instant.FromUtc(2026, 10, 17, 0, 0), Ct);
    }

    [Fact]
    public async Task RunAsync_ReportsTheDeletedCountAndCutoffAsTheResultAndInTheLogAsync()
    {
        store.DeleteFinishedBeforeAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(7);

        var result = await RunAsync();

        result.Should().Be(new WorkItemRetentionResult(7, Instant.FromUtc(2026, 9, 18, 12, 0)));
        logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(entry => entry.Level == LogLevel.Information && entry.Message.Contains('7'));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(6)]
    public void Schedule_IsTheRetentionKeyAtTheConfiguredInterval(double hours)
        => new WorkItemRetentionSchedule(Options.Create(new WorkerSettings { WorkItemRetentionIntervalHours = hours }))
            .Should().BeEquivalentTo(new { DedupKey = "work-item-retention", Interval = Duration.FromHours(hours) });
}
