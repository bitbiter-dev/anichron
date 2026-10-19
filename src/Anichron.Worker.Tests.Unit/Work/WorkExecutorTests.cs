using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.DependencyInjection;
using Anichron.Testing.Time;
using Anichron.Worker.Crawling;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Work;

public sealed class WorkExecutorTests : IAsyncDisposable
{
    public sealed record Cleanup : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.TokenCleanup;
    }

    public sealed record Retention : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.WorkItemRetention;
    }

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
    private static readonly Duration Lease = Duration.FromSeconds(60);

    private readonly Timeline timeline = new();
    private readonly IWorkItemStore store = Substitute.For<IWorkItemStore>();
    private readonly IWorkHandler<Cleanup> handler = Substitute.For<IWorkHandler<Cleanup>>();
    private readonly FakeLogger<WorkExecutor> logger = new();
    private readonly Queue<ClaimedWork> queued = new();
    private readonly WorkerState workerState = new();
    private readonly List<TaskCompletionSource<object?>> runs = [];
    private CancellationToken handlerToken;
    private IWorkProgress? handlerProgress;
    private WorkExecutor? executor;

    public WorkExecutorTests()
    {
        store.ClaimAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("claim");
                return queued.TryDequeue(out var work) ? work : null;
            });
        store.HeartbeatAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("heartbeat");
                return true;
            });
        store.When(s => s.SucceedAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<string?>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("succeed"));
        store.When(s => s.FailAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<string>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("fail"));
        store.When(s => s.ReleaseAsync(Arg.Any<WorkAttempt>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("release"));
        handler.RunAsync(Arg.Any<Cleanup>(), Arg.Any<IWorkProgress>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handlerProgress = call.Arg<IWorkProgress>();
                handlerToken = call.Arg<CancellationToken>();
                var run = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                handlerToken.Register(() => run.TrySetCanceled(handlerToken));
                runs.Add(run);
                timeline.Record("run");
                return run.Task;
            });
    }

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Instant Now => Instant.FromDateTimeOffset(timeline.Clock.GetUtcNow());

    private static ClaimedWork Work(int attempt = 1)
        => new(new WorkAttempt(Guid.Parse("33333333-3333-3333-3333-333333333333"), attempt), WorkKind.TokenCleanup, "{}");

    private async Task StartAsync(params ClaimedWork[] items)
    {
        foreach (var item in items)
            queued.Enqueue(item);
        var registry = new WorkHandlerRegistry(
            [WorkHandlerRegistration.For<Cleanup>(), WorkHandlerRegistration.For<Retention>()],
            [typeof(Cleanup), typeof(Retention)]);
        executor = new WorkExecutor(
            store, registry, workerState, ScopeFactories.Providing(services => services.AddSingleton(handler)),
            timeline.Clock, Options.Create(new WorkerSettings()), logger);
        await executor.StartAsync(Ct);
        timeline.Watch(executor.ExecuteTask!, () => executor.StopAsync(CancellationToken.None));
    }

    private async Task StartRunningAsync()
    {
        await StartAsync(Work());
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Heartbeat), new Called("run"));
    }

    // ==========================================================================
    // Claiming
    // ==========================================================================

    [Fact]
    public async Task ExecuteAsync_NothingQueued_ClaimsAgainAfterThePollIntervalAsync()
    {
        await StartAsync();
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));

        timeline.Clock.Advance(Poll);

        await timeline.ExpectAsync(new TimerFired(Poll), new Called("claim"), new TimerArmed(Poll));
    }

    [Fact]
    public async Task ExecuteAsync_ClaimsAsThisWorkerWithTheConfiguredLeaseAsync()
    {
        await StartAsync();
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));

        await store.Received(1).ClaimAsync($"all@{Environment.MachineName}", null, Now, Lease, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ClaimsForTheUserResolvedAtThatMomentAsync()
    {
        var mira = Guid.Parse("44444444-4444-4444-4444-444444444444");
        await StartAsync();
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));

        workerState.ResolvedUserId = mira;
        timeline.Clock.Advance(Poll);

        await timeline.ExpectAsync(new TimerFired(Poll), new Called("claim"), new TimerArmed(Poll));
        store.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IWorkItemStore.ClaimAsync))
            .Select(call => (Guid?)call.GetArguments()[1])
            .Should().Equal(null, mira);
    }

    [Theory]
    [InlineData("", "all@nas")]
    [InlineData("mira", "mira@nas")]
    public void ClaimantName_IsTheWorkersUserOrAllThenItsHost(string user, string expected)
        => WorkExecutor.ClaimantName(new WorkerSettings { User = user }, "nas").Should().Be(expected);

    [Fact]
    public async Task ExecuteAsync_ClaimFails_LogsAndClaimsAgainAtTheNextPollAsync()
    {
        var calls = 0;
        store.ClaimAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns<ClaimedWork?>(_ =>
            {
                timeline.Record("claim");
                return ++calls == 1 ? throw new InvalidOperationException("db down") : null;
            });
        await StartAsync();
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));

        timeline.Clock.Advance(Poll);

        await timeline.ExpectAsync(new TimerFired(Poll), new Called("claim"), new TimerArmed(Poll));
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExecuteAsync_TwoItemsQueued_RunsThemOneAtATimeAsync()
    {
        await StartAsync(Work(), Work(attempt: 2));
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Heartbeat), new Called("run"));

        runs[0].SetResult(null);

        await timeline.ExpectAsync(
            new Called("succeed"), new Called("claim"), new TimerArmed(Heartbeat), new Called("run"));
        runs[1].SetResult(null);
        await timeline.ExpectAsync(new Called("succeed"), new Called("claim"), new TimerArmed(Poll));
    }

    // ==========================================================================
    // Heartbeat
    // ==========================================================================

    [Fact]
    public async Task ExecuteAsync_Running_HeartbeatsAtTheIntervalAsync()
    {
        await StartRunningAsync();
        timeline.Clock.Advance(Heartbeat - TimeSpan.FromTicks(1));
        timeline.Unread.Should().BeEmpty();

        timeline.Clock.Advance(TimeSpan.FromTicks(1));
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
    }

    [Fact]
    public async Task ExecuteAsync_Heartbeat_ExtendsTheLeaseAndSendsTheReportedProgressAsync()
    {
        await StartRunningAsync();
        handlerProgress!.Report(3, 8, "walking");

        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(new TimerFired(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        await store.Received(1).HeartbeatAsync(
            Work().Attempt, new WorkProgress(3, 8, "walking"), Now, Lease, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatFails_LogsAndKeepsHeartbeatingAsync()
    {
        var beats = 0;
        store.HeartbeatAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("heartbeat");
                return ++beats == 1 ? throw new InvalidOperationException("db down") : true;
            });
        await StartRunningAsync();

        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(new TimerFired(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        handlerToken.IsCancellationRequested.Should().BeFalse();
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatFenced_CancelsTheHandlerAndWritesNothingMoreAsync()
    {
        store.HeartbeatAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("heartbeat");
                return false;
            });
        await StartRunningAsync();

        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(
            new TimerFired(Heartbeat), new Called("heartbeat"), new Called("claim"), new TimerArmed(Poll));
        handlerToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_FencedHandlerFinishesAnyway_WritesNothingAsync()
    {
        var fenced = false;
        store.HeartbeatAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("heartbeat");
                fenced = runs[0].TrySetResult("done anyway");
                return false;
            });
        await StartRunningAsync();

        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(
            new TimerFired(Heartbeat), new Called("heartbeat"), new Called("claim"), new TimerArmed(Poll));
        fenced.Should().BeTrue();
    }

    // ==========================================================================
    // Outcomes
    // ==========================================================================

    [Fact]
    public async Task ExecuteAsync_HandlerSucceeds_StoresItsResultAndFinalProgressAsync()
    {
        await StartRunningAsync();
        handlerProgress!.Report(3, 3, "done");

        runs[0].SetResult(new { Deleted = 3 });

        await timeline.ExpectAsync(new Called("succeed"), new Called("claim"), new TimerArmed(Poll));
        await store.Received(1).SucceedAsync(
            Work().Attempt, new WorkProgress(3, 3, "done"), /*lang=json,strict*/ """{"deleted":3}""", Now, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerReturnsNoResult_StoresNoneAsync()
    {
        await StartRunningAsync();

        runs[0].SetResult(null);

        await timeline.ExpectAsync(new Called("succeed"), new Called("claim"), new TimerArmed(Poll));
        await store.Received(1).SucceedAsync(Work().Attempt, WorkProgress.None, null, Now, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerThrows_FailsTheItemWithTheErrorAndLastProgressAsync()
    {
        await StartRunningAsync();
        handlerProgress!.Report(1, null, "walking");

        runs[0].SetException(new InvalidOperationException("boom"));

        await timeline.ExpectAsync(new Called("fail"), new Called("claim"), new TimerArmed(Poll));
        await store.Received(1).FailAsync(
            Work().Attempt, new WorkProgress(1, null, "walking"), "InvalidOperationException: boom", Now, CancellationToken.None);
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerCancelsItselfWithoutAShutdown_FailsTheItemAsync()
    {
        await StartRunningAsync();

        runs[0].SetCanceled(CancellationToken.None);

        await timeline.ExpectAsync(new Called("fail"), new Called("claim"), new TimerArmed(Poll));
        await store.Received(1).FailAsync(
            Work().Attempt, WorkProgress.None, "TaskCanceledException: A task was canceled.", Now, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_HandlerThrowsSynchronously_FailsTheItemAsync()
    {
        handler.RunAsync(Arg.Any<Cleanup>(), Arg.Any<IWorkProgress>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("run");
                throw new InvalidOperationException("bad payload");
            });
        await StartAsync(Work());

        await timeline.ExpectAsync(
            new Called("claim"), new TimerArmed(Heartbeat), new Called("run"), new Called("fail"),
            new Called("claim"), new TimerArmed(Poll));
    }

    [Fact]
    public async Task StopAsync_WhileRunning_CancelsTheHandlerAndReleasesTheItemAsync()
    {
        await StartRunningAsync();

        await executor!.StopAsync(Ct);

        await timeline.ExpectAsync(new Called("release"), new Finished(TaskStatus.Canceled));
        handlerToken.IsCancellationRequested.Should().BeTrue();
        await store.Received(1).ReleaseAsync(Work().Attempt, Now, CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhileWaitingToPoll_EndsWithoutFaultingAsync()
    {
        await StartAsync();
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));

        await executor!.StopAsync(Ct);

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
    }
}
