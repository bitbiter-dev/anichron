using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Testing.DependencyInjection;
using Anichron.Testing.Time;
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

    public sealed record Crawl : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.Crawl;
    }

    public sealed record Bursts : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.BurstDetection;
    }

    public sealed record Sweep : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.ProxySweep;
    }

    public sealed record Deletion : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.ProxyDeletion;
    }

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
    private static readonly Duration Lease = Duration.FromSeconds(60);
    private static readonly TimeSpan SelfStop = TimeSpan.FromSeconds(30);

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
        Heartbeats(() => HeartbeatOutcome.Held);
        store.When(s => s.SucceedAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<string?>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("succeed"));
        store.When(s => s.FailAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<string>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("fail"));
        store.When(s => s.CancelAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<string?>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("cancel"));
        store.When(s => s.ReleaseAsync(Arg.Any<WorkAttempt>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>()))
            .Do(_ => timeline.Record("release"));
        handler.RunAsync(Arg.Any<Cleanup>(), Arg.Any<WorkRun>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handlerProgress = call.Arg<WorkRun>().Progress;
                handlerToken = call.Arg<CancellationToken>();
                var run = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                handlerToken.Register(() => run.TrySetCanceled(handlerToken));
                runs.Add(run);
                timeline.Record("run");
                return run.Task;
            });
    }

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private void Heartbeats(Func<HeartbeatOutcome> beat)
        => store.HeartbeatAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                timeline.Record("heartbeat");
                await Task.Yield();
                return beat();
            });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Instant Now => Instant.FromDateTimeOffset(timeline.Clock.GetUtcNow());

    private static ClaimedWork Work(int attempt = 1)
        => new(new WorkAttempt(Guid.Parse("33333333-3333-3333-3333-333333333333"), attempt), WorkKind.TokenCleanup, "{}");

    private Task StartAsync(params ClaimedWork[] items) => StartAsync(new WorkerSettings(), items);

    private async Task StartAsync(WorkerSettings settings, params ClaimedWork[] items)
    {
        foreach (var item in items)
            queued.Enqueue(item);
        var registry = new WorkHandlerRegistry(
            [
                WorkHandlerRegistration.For<Cleanup>(), WorkHandlerRegistration.For<Retention>(),
                WorkHandlerRegistration.For<Crawl>(), WorkHandlerRegistration.For<Bursts>(),
                WorkHandlerRegistration.For<Sweep>(), WorkHandlerRegistration.For<Deletion>(),
            ],
            [typeof(Cleanup), typeof(Retention), typeof(Crawl), typeof(Bursts), typeof(Sweep), typeof(Deletion)]);
        executor = new WorkExecutor(
            store, registry, workerState, ScopeFactories.Providing(services => services.AddSingleton(handler)),
            timeline.Clock, Options.Create(settings), logger);
        await executor.StartAsync(Ct);
        timeline.Watch(executor.ExecuteTask!, () => executor.StopAsync(CancellationToken.None));
    }

    private Task StopAsync() => executor!.StopAsync(Ct).WaitAsync(Timeline.DefaultSilenceLimit, Ct);

    private async Task StartRunningAsync()
    {
        await StartAsync(Work());
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(SelfStop), new TimerArmed(Heartbeat), new Called("run"));
    }

    private static TimelineEvent[] Beat()
        => [new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat)];

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
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(SelfStop), new TimerArmed(Heartbeat), new Called("run"));

        runs[0].SetResult(null);

        await timeline.ExpectAsync(
            new Called("succeed"), new Called("claim"), new TimerArmed(SelfStop), new TimerArmed(Heartbeat), new Called("run"));
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
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
    }

    [Fact]
    public async Task ExecuteAsync_Heartbeat_ExtendsTheLeaseAndSendsTheReportedProgressAsync()
    {
        await StartRunningAsync();
        handlerProgress!.Report(3, 8, "walking");

        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        await store.Received(1).HeartbeatAsync(
            Work().Attempt, new WorkProgress(3, 8, "walking"), Now, Lease, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatFails_LogsAndKeepsHeartbeatingAsync()
    {
        var beats = 0;
        Heartbeats(() => ++beats == 1 ? throw new InvalidOperationException("db down") : HeartbeatOutcome.Held);
        await StartRunningAsync();

        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        handlerToken.IsCancellationRequested.Should().BeFalse();
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatFenced_CancelsTheHandlerAndWritesNothingMoreAsync()
    {
        Heartbeats(() => HeartbeatOutcome.Lost);
        await StartRunningAsync();

        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(
            new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new Called("claim"), new TimerArmed(Poll));
        handlerToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_FencedHandlerFinishesAnyway_WritesNothingAsync()
    {
        var fenced = false;
        Heartbeats(() =>
        {
            fenced = runs[0].TrySetResult("done anyway");
            return HeartbeatOutcome.Lost;
        });
        await StartRunningAsync();

        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(
            new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new Called("claim"), new TimerArmed(Poll));
        fenced.Should().BeTrue();
    }

    // ==========================================================================
    // Self-stop
    // ==========================================================================

    private static readonly TimeSpan ShortSelfStop = TimeSpan.FromSeconds(4.5);

    private async Task StartRunningWithAShortLeaseAsync()
    {
        await StartAsync(new WorkerSettings { WorkHeartbeatSeconds = 2, WorkLeaseSeconds = 9 }, Work());
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(ShortSelfStop), new TimerArmed(Heartbeat), new Called("run"));
    }

    private void HeartbeatsFailWhile(Func<bool> down)
        => Heartbeats(() => down() ? throw new InvalidOperationException("db down") : HeartbeatOutcome.Held);

    private void HeartbeatsNeverAnswer()
        => store.HeartbeatAsync(
                Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<Instant>(), Arg.Any<Duration>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("heartbeat");
                var never = new TaskCompletionSource<HeartbeatOutcome>();
                return never.Task;
            });

    private async Task ExpectSelfStoppedAsync()
    {
        await timeline.ExpectAsync(new TimerFired(ShortSelfStop), new Called("claim"), new TimerArmed(Poll));
        handlerToken.IsCancellationRequested.Should().BeTrue();
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("stopped itself"))
            .Which.Message.Should().Be(
                $"Work item {Work().Attempt.Id} stopped itself: no heartbeat reached the database for half a lease, so its lease may already be recovered by another Worker; this attempt wrote nothing more.");
        store.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .Should().OnlyContain(name => name == nameof(IWorkItemStore.ClaimAsync) || name == nameof(IWorkItemStore.HeartbeatAsync));
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatsFailForHalfALease_CancelsTheHandlerThenAndWritesNothingAsync()
    {
        var down = true;
        HeartbeatsFailWhile(() => down);
        await StartRunningWithAShortLeaseAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(Beat());
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(Beat());

        timeline.Clock.Advance(ShortSelfStop - (Heartbeat * 2) - TimeSpan.FromTicks(1));
        handlerToken.IsCancellationRequested.Should().BeFalse();
        down = false;
        timeline.Clock.Advance(TimeSpan.FromTicks(1));

        await ExpectSelfStoppedAsync();
    }

    [Fact]
    public async Task ExecuteAsync_AHeartbeatSucceeds_RestartsTheHalfLeaseFromThereAsync()
    {
        var beats = 0;
        Heartbeats(() => ++beats == 2 ? HeartbeatOutcome.Held : throw new InvalidOperationException("db down"));
        await StartRunningWithAShortLeaseAsync();
        for (var beat = 0; beat < 4; beat++)
        {
            timeline.Clock.Advance(Heartbeat);
            await timeline.ExpectAsync(Beat());
        }

        timeline.Clock.Advance((Heartbeat * 2) + ShortSelfStop - (Heartbeat * 4) - TimeSpan.FromTicks(1));
        handlerToken.IsCancellationRequested.Should().BeFalse();
        timeline.Clock.Advance(TimeSpan.FromTicks(1));

        await ExpectSelfStoppedAsync();
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatNeverAnswers_StillCancelsTheHandlerAtHalfALeaseAsync()
    {
        HeartbeatsNeverAnswer();
        await StartRunningWithAShortLeaseAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"));

        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat));

        timeline.Clock.Advance(ShortSelfStop - (Heartbeat * 2));

        await ExpectSelfStoppedAsync();
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatNeverAnswers_GivesUpOnItAfterOneIntervalAndBeatsAgainAsync()
    {
        HeartbeatsNeverAnswer();
        await StartRunningAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"));

        timeline.Clock.Advance(Heartbeat - TimeSpan.FromTicks(1));
        timeline.Unread.Should().BeEmpty();
        timeline.Clock.Advance(TimeSpan.FromTicks(1));
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat));
        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"));
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_HeartbeatCallIsLimitedToOneIntervalAsync()
    {
        HeartbeatsNeverAnswer();
        await StartRunningAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"));

        var token = (CancellationToken)store.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IWorkItemStore.HeartbeatAsync)).GetArguments()[4]!;
        token.IsCancellationRequested.Should().BeFalse();
        timeline.Clock.Advance(Heartbeat);

        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat));
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhileAHeartbeatHangs_ReleasesTheItemWithoutWaitingForItAsync()
    {
        HeartbeatsNeverAnswer();
        await StartRunningAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"));

        await StopAsync();

        await timeline.ExpectAsync(new Called("release"), new Finished(TaskStatus.Canceled));
        logger.Collector.GetSnapshot().Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ExecuteAsync_SelfStoppedHandlerFinishesAnyway_WritesNothingAsync()
    {
        HeartbeatsFailWhile(() => true);
        HandlerWindsDownOnlyWhenTold();
        await StartRunningWithAShortLeaseAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(Beat());
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(Beat());
        timeline.Clock.Advance(ShortSelfStop - (Heartbeat * 2));
        await timeline.ExpectAsync(new TimerFired(ShortSelfStop));

        runs[0].SetResult("done anyway");

        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));
        await store.DidNotReceive().SucceedAsync(
            Arg.Any<WorkAttempt>(), Arg.Any<WorkProgress>(), Arg.Any<string?>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>());
    }

    // ==========================================================================
    // Cancel requests
    // ==========================================================================

    private void HeartbeatsReportACancelRequest()
        => Heartbeats(() => HeartbeatOutcome.CancelRequested);

    private void HandlerWindsDownOnlyWhenTold()
        => handler.RunAsync(Arg.Any<Cleanup>(), Arg.Any<WorkRun>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                handlerProgress = call.Arg<WorkRun>().Progress;
                handlerToken = call.Arg<CancellationToken>();
                var run = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                runs.Add(run);
                timeline.Record("run");
                return run.Task;
            });

    private async Task CancelRequestedWhileRunningAsync()
    {
        HeartbeatsReportACancelRequest();
        HandlerWindsDownOnlyWhenTold();
        await StartRunningAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
    }

    [Fact]
    public async Task ExecuteAsync_CancelRequested_CancelsTheHandlerAndKeepsHoldingTheLeaseAsync()
    {
        await CancelRequestedWhileRunningAsync();

        handlerToken.IsCancellationRequested.Should().BeTrue();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));
        runs[0].SetCanceled(handlerToken);
        await timeline.ExpectAsync(new Called("cancel"), new Called("claim"), new TimerArmed(Poll));
    }

    [Fact]
    public async Task ExecuteAsync_CancelRequestedAndHandlerStops_CancelsTheItemWithItsLastProgressAsync()
    {
        await CancelRequestedWhileRunningAsync();
        handlerProgress!.Report(4, null, "walking");

        runs[0].SetCanceled(handlerToken);

        await timeline.ExpectAsync(new Called("cancel"), new Called("claim"), new TimerArmed(Poll));
        await store.Received(1).CancelAsync(
            Work().Attempt, new WorkProgress(4, null, "walking"), null, Now, CancellationToken.None);
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Information)
            .Which.Message.Should().Be($"Work item {Work().Attempt.Id} was cancelled at an admin's request.");
    }

    [Fact]
    public async Task ExecuteAsync_CancelRequestedButHandlerReturnsASummary_KeepsItAsThePartialResultAsync()
    {
        await CancelRequestedWhileRunningAsync();

        runs[0].SetResult(new { Deleted = 2 });

        await timeline.ExpectAsync(new Called("cancel"), new Called("claim"), new TimerArmed(Poll));
        await store.Received(1).CancelAsync(
            Work().Attempt, WorkProgress.None, /*lang=json,strict*/ """{"deleted":2}""", Now, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_CancelRequestedButHandlerFails_FailsTheItemAsync()
    {
        await CancelRequestedWhileRunningAsync();

        runs[0].SetException(new InvalidOperationException("boom"));

        await timeline.ExpectAsync(new Called("fail"), new Called("claim"), new TimerArmed(Poll));
    }

    [Fact]
    public async Task StopAsync_AfterACancelRequest_CancelsTheItemInsteadOfReleasingItAsync()
    {
        await CancelRequestedWhileRunningAsync();

        var stopping = StopAsync();
        runs[0].SetCanceled(handlerToken);
        await stopping;

        await timeline.ExpectAsync(new Called("cancel"), new Called("claim"), new Finished(TaskStatus.Canceled));
        await store.DidNotReceive().ReleaseAsync(Arg.Any<WorkAttempt>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CancelRequestedThenFenced_WritesNothingAsync()
    {
        var beats = 0;
        Heartbeats(() => ++beats == 1 ? HeartbeatOutcome.CancelRequested : HeartbeatOutcome.Lost);
        HandlerWindsDownOnlyWhenTold();
        await StartRunningAsync();
        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"), new TimerArmed(Heartbeat));

        timeline.Clock.Advance(Heartbeat);
        await timeline.ExpectAsync(new TimerFired(Heartbeat), new TimerArmed(Heartbeat), new Called("heartbeat"));
        runs[0].SetCanceled(handlerToken);

        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));
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
        handler.RunAsync(Arg.Any<Cleanup>(), Arg.Any<WorkRun>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("run");
                throw new InvalidOperationException("bad payload");
            });
        await StartAsync(Work());

        await timeline.ExpectAsync(
            new Called("claim"), new TimerArmed(SelfStop), new TimerArmed(Heartbeat), new Called("run"), new Called("fail"),
            new Called("claim"), new TimerArmed(Poll));
    }

    [Fact]
    public async Task StopAsync_WhileRunning_CancelsTheHandlerAndReleasesTheItemAsync()
    {
        await StartRunningAsync();

        await StopAsync();

        await timeline.ExpectAsync(new Called("release"), new Finished(TaskStatus.Canceled));
        handlerToken.IsCancellationRequested.Should().BeTrue();
        await store.Received(1).ReleaseAsync(Work().Attempt, Now, CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_WhileWaitingToPoll_EndsWithoutFaultingAsync()
    {
        await StartAsync();
        await timeline.ExpectAsync(new Called("claim"), new TimerArmed(Poll));

        await StopAsync();

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
    }
}
