using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Maintenance;

public sealed class TokenCleanupTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 5, 12, 10, 0, 0);

    private readonly IRefreshTokenRepository repository = Substitute.For<IRefreshTokenRepository>();
    private readonly IClock clock = Substitute.For<IClock>();
    private readonly FakeLogger<TokenCleanupHandler> logger = new();

    public TokenCleanupTests() => clock.GetCurrentInstant().Returns(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<object?> RunAsync()
        => new TokenCleanupHandler(repository, clock, logger)
            .RunAsync(new TokenCleanupPayload(), Substitute.For<IWorkProgress>(), Ct);

    [Fact]
    public async Task RunAsync_DeletesTokensExpiredBeforeNowAsync()
    {
        await RunAsync();

        await repository.Received(1).DeleteExpiredAsync(Now, Ct);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task RunAsync_ReportsTheDeletedCountAsTheResultAndInTheLogAsync(int deletedCount)
    {
        repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>()).Returns(deletedCount);

        var result = await RunAsync();

        result.Should().Be(new TokenCleanupResult(deletedCount));
        logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(entry => entry.Level == LogLevel.Information
                && entry.Message.Contains(deletedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task RunAsync_RepositoryFails_PropagatesSoTheItemFailsAsync()
    {
        repository.DeleteExpiredAsync(Arg.Any<Instant>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("DB error"));

        var act = RunAsync;

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB error");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(2.5)]
    public void Schedule_IsTheTokenCleanupKeyAtTheConfiguredInterval(double hours)
        => new TokenCleanupSchedule(Options.Create(new WorkerSettings { TokenCleanupIntervalHours = hours }))
            .Should().BeEquivalentTo(new { DedupKey = "token-cleanup", Interval = Duration.FromHours(hours) });
}
