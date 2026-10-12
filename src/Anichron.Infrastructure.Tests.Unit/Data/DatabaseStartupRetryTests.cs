using Anichron.Infrastructure.Data;
using Microsoft.Extensions.Time.Testing;

namespace Anichron.Infrastructure.Tests.Unit.Data;

public sealed class DatabaseStartupRetryTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider time = new();
    private readonly List<(Exception Exception, int Attempt)> retries = [];
    private readonly List<Exception> giveUps = [];

    private Task RunAsync(Func<CancellationToken, Task> operation, int maxAttempts = 3, CancellationToken? ct = null)
        => new DatabaseStartupRetry(time, maxAttempts, Delay)
            .RunAsync(operation, (ex, attempt) => retries.Add((ex, attempt)), giveUps.Add, ct ?? TestContext.Current.CancellationToken);

    [Fact]
    public async Task RunAsync_FirstAttemptSucceeds_RunsOnceWithoutRetryingAsync()
    {
        var calls = 0;

        await RunAsync(_ => { calls++; return Task.CompletedTask; });

        calls.Should().Be(1);
        retries.Should().BeEmpty();
        giveUps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_FirstAttemptFails_WaitsTheFullDelayBeforeRetryingAsync()
    {
        var failure = new InvalidOperationException("not ready");
        var calls = 0;

        var run = RunAsync(_ => ++calls == 1 ? Task.FromException(failure) : Task.CompletedTask);

        calls.Should().Be(1);
        retries.Should().Equal((failure, 1));
        time.Advance(Delay - TimeSpan.FromTicks(1));
        run.IsCompleted.Should().BeFalse();
        calls.Should().Be(1);

        time.Advance(TimeSpan.FromTicks(1));
        await run.WaitAsync(Settle, TestContext.Current.CancellationToken);

        calls.Should().Be(2);
        giveUps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_EveryAttemptFails_GivesUpAfterMaxAttemptsWrappingTheLastErrorAsync()
    {
        var failures = new[] { new TimeoutException("1"), new TimeoutException("2"), new TimeoutException("3") };
        var calls = 0;

        var run = RunAsync(_ => Task.FromException(failures[calls++]));
        time.Advance(Delay);
        time.Advance(Delay);
        var act = () => run.WaitAsync(Settle, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Database unavailable after 3 attempts. Aborting."))
            .WithInnerException<TimeoutException>().WithMessage("3");
        calls.Should().Be(3);
        retries.Should().Equal((failures[0], 1), (failures[1], 2));
        giveUps.Should().Equal(failures[2]);
    }

    [Fact]
    public async Task RunAsync_CancelledWhileWaiting_StopsWithoutAnotherAttemptAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var calls = 0;

        var run = RunAsync(_ => { calls++; return Task.FromException(new InvalidOperationException()); }, ct: cts.Token);
        await cts.CancelAsync();
        var act = () => run.WaitAsync(Settle, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
        giveUps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PassesTheCallersTokenToTheOperationAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        CancellationToken seen = default;

        await RunAsync(token => { seen = token; return Task.CompletedTask; }, ct: cts.Token);

        seen.Should().Be(cts.Token);
    }
}
