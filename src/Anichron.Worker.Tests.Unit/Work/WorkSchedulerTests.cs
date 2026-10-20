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

public sealed class WorkSchedulerTests : IAsyncDisposable
{
    public sealed record Keyed(string Key) : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.TokenCleanup;

        string IWorkPayload.DedupKey => Key;
    }

    private sealed class KeyedSchedule(string key, Duration interval)
        : WorkSchedule<Keyed>(new Keyed(key), interval);

    private sealed class ChangingSource(Func<IReadOnlyList<IWorkSchedule>> current) : IWorkScheduleSource
    {
        public Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(IServiceProvider services, CancellationToken ct)
            => Task.FromResult(current());
    }

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
    private static readonly Duration Hourly = Duration.FromHours(1);
    private static readonly Duration Daily = Duration.FromDays(1);

    private readonly Timeline timeline = new();
    private readonly IWorkItemStore store = Substitute.For<IWorkItemStore>();
    private readonly IWorkQueue queue = Substitute.For<IWorkQueue>();
    private readonly FakeLogger<WorkScheduler> logger = new();
    private readonly HashSet<string> due = [];
    private WorkScheduler? scheduler;

    public WorkSchedulerTests()
    {
        store.RecoverExpiredLeasesAsync(Arg.Any<Instant>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("recover");
                return Task.CompletedTask;
            });
        store.IsDueAsync(Arg.Any<string>(), Arg.Any<Instant>(), Arg.Any<CancellationToken>())
            .Returns(call => due.Contains(call.Arg<string>()));
        queue.EnqueueAsync(Arg.Any<Keyed>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                timeline.Record($"enqueue {call.Arg<Keyed>().Key}");
                return Guid.NewGuid();
            });
    }

    public ValueTask DisposeAsync() => timeline.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Instant Now => Instant.FromDateTimeOffset(timeline.Clock.GetUtcNow());

    private async Task StartAsync(params IWorkScheduleSource[] sources)
    {
        scheduler = new WorkScheduler(
            store, sources, ScopeFactories.Providing(services => services.AddSingleton(queue)),
            timeline.Clock, Options.Create(new WorkerSettings()), logger);
        await scheduler.StartAsync(Ct);
        timeline.Watch(scheduler.ExecuteTask!, () => scheduler.StopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Started_RecoversLeasesThenEnqueuesWhatIsDueAsync()
    {
        due.Add("sweep");

        await StartAsync(new KeyedSchedule("sweep", Hourly), new KeyedSchedule("purge", Daily));

        await timeline.ExpectAsync(new Called("recover"), new Called("enqueue sweep"), new TimerArmed(Poll));
        await store.Received(1).RecoverExpiredLeasesAsync(Now, 3, Arg.Any<CancellationToken>());
        await queue.Received(1).EnqueueAsync(new Keyed("sweep"), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AsksEachScheduleWhetherItsLastRunIsOlderThanItsIntervalAsync()
    {
        await StartAsync(new KeyedSchedule("sweep", Hourly), new KeyedSchedule("purge", Daily));
        await timeline.ExpectAsync(new Called("recover"), new TimerArmed(Poll));

        await store.Received(1).IsDueAsync("sweep", Now - Hourly, Arg.Any<CancellationToken>());
        await store.Received(1).IsDueAsync("purge", Now - Daily, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PollIntervalElapses_TicksAgainAtTheLaterTimeAsync()
    {
        await StartAsync(new KeyedSchedule("sweep", Hourly));
        await timeline.ExpectAsync(new Called("recover"), new TimerArmed(Poll));
        due.Add("sweep");

        timeline.Clock.Advance(Poll);

        await timeline.ExpectAsync(
            new TimerFired(Poll), new Called("recover"), new Called("enqueue sweep"), new TimerArmed(Poll));
        await store.Received(1).IsDueAsync("sweep", Now - Hourly, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AsksEachSourceForItsSchedulesAtEveryTickAsync()
    {
        IReadOnlyList<IWorkSchedule> current = [new KeyedSchedule("crawl:a", Hourly)];
        due.UnionWith(["crawl:a", "crawl:b"]);
        await StartAsync(new ChangingSource(() => current));
        await timeline.ExpectAsync(new Called("recover"), new Called("enqueue crawl:a"), new TimerArmed(Poll));
        current = [new KeyedSchedule("crawl:a", Hourly), new KeyedSchedule("crawl:b", Hourly)];

        timeline.Clock.Advance(Poll);

        await timeline.ExpectAsync(
            new TimerFired(Poll), new Called("recover"), new Called("enqueue crawl:a"), new Called("enqueue crawl:b"),
            new TimerArmed(Poll));
    }

    [Fact]
    public async Task WorkSchedule_IsItsOwnOnlySourceAsync()
    {
        var schedule = new KeyedSchedule("sweep", Hourly);

        (await schedule.SchedulesAsync(Substitute.For<IServiceProvider>(), Ct)).Should().Equal(schedule);
    }

    [Fact]
    public async Task ExecuteAsync_TickFails_LogsAndTicksAgainAtTheNextPollAsync()
    {
        var calls = 0;
        store.RecoverExpiredLeasesAsync(Arg.Any<Instant>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                timeline.Record("recover");
                return ++calls == 1 ? throw new InvalidOperationException("db down") : Task.CompletedTask;
            });
        await StartAsync();
        await timeline.ExpectAsync(new Called("recover"), new TimerArmed(Poll));

        timeline.Clock.Advance(Poll);

        await timeline.ExpectAsync(new TimerFired(Poll), new Called("recover"), new TimerArmed(Poll));
        logger.Collector.GetSnapshot().Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task StopAsync_EndsWithoutFaultingAsync()
    {
        await StartAsync();
        await timeline.ExpectAsync(new Called("recover"), new TimerArmed(Poll));

        await scheduler!.StopAsync(Ct);

        await timeline.ExpectAsync(new Finished(TaskStatus.Canceled));
    }

    [Fact]
    public void WorkSchedule_TakesItsKeyFromThePayload()
        => new KeyedSchedule("sweep", Hourly).Should().BeEquivalentTo(new { DedupKey = "sweep", Interval = Hourly });

    [Fact]
    public void WorkSchedule_PayloadWithoutAKey_IsRejected()
    {
        var act = () => new UnkeyedSchedule();

        act.Should().Throw<InvalidOperationException>().WithMessage("A periodic WorkItemRetention item needs a dedup key.");
    }

    public sealed record Unkeyed : IWorkPayload
    {
        public static WorkKind Kind => WorkKind.WorkItemRetention;
    }

    private sealed class UnkeyedSchedule() : WorkSchedule<Unkeyed>(new Unkeyed(), Duration.FromHours(1));
}
