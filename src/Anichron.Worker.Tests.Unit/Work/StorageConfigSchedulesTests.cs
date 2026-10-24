using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Work;

public sealed class StorageConfigSchedulesTests
{
    private static readonly Guid MiraId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly IUserStorageConfigRepository configs = Substitute.For<IUserStorageConfigRepository>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserStorageConfig Config() => new()
    {
        Id = Guid.NewGuid(),
        UserId = MiraId,
        RootPath = "/data/originals/mira",
        IsActive = true,
    };

    private async Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(IWorkScheduleSource source)
    {
        await using var services = new ServiceCollection().AddSingleton(configs).BuildServiceProvider();
        return await source.SchedulesAsync(services, Ct);
    }

    [Fact]
    public async Task SchedulesAsync_AllUserWorker_SchedulesEveryActiveConfigAtTheCrawlIntervalAsync()
    {
        var (a, b) = (Config(), Config());
        configs.GetAllActiveAsync(Ct).Returns([a, b]);
        var source = new StorageConfigSchedules<CrawlPayload>(
            new WorkerState(), Options.Create(new WorkerSettings { CrawlIntervalHours = 6 }));

        var schedules = await SchedulesAsync(source);

        schedules.Select(s => (s.DedupKey, s.Interval)).Should().Equal(
            ($"crawl:{a.Id}", Duration.FromHours(6)), ($"crawl:{b.Id}", Duration.FromHours(6)));
    }

    [Fact]
    public async Task SchedulesAsync_PerUserWorker_SchedulesOnlyItsUsersActiveConfigsAsync()
    {
        var mine = Config();
        configs.GetActiveByUserIdAsync(MiraId, Ct).Returns([mine]);
        configs.GetAllActiveAsync(Ct).Returns([mine, Config()]);
        var state = new WorkerState();
        var source = new StorageConfigSchedules<CrawlPayload>(state, Options.Create(new WorkerSettings()));
        state.ResolvedUserId = MiraId;

        var schedules = await SchedulesAsync(source);

        schedules.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { DedupKey = $"crawl:{mine.Id}", Interval = Duration.FromHours(4) });
    }

    [Fact]
    public async Task SchedulesAsync_BurstDetection_SchedulesItPerConfigAtTheCrawlIntervalAsync()
    {
        var config = Config();
        configs.GetAllActiveAsync(Ct).Returns([config]);
        var source = new StorageConfigSchedules<BurstDetectionPayload>(
            new WorkerState(), Options.Create(new WorkerSettings { CrawlIntervalHours = 6 }));

        var schedules = await SchedulesAsync(source);

        schedules.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { DedupKey = $"burst-detection:{config.Id}", Interval = Duration.FromHours(6) });
    }

    [Fact]
    public async Task Schedule_EnqueuesItsConfigsPayloadAsync()
    {
        var configId = Guid.NewGuid();
        var queue = Substitute.For<IWorkQueue>();

        configs.GetAllActiveAsync(Ct).Returns([new UserStorageConfig { Id = configId, UserId = MiraId, RootPath = "/r", IsActive = true }]);
        var schedules = await SchedulesAsync(
            new StorageConfigSchedules<BurstDetectionPayload>(new WorkerState(), Options.Create(new WorkerSettings())));

        await schedules.Single().EnqueueAsync(queue, Ct);

        await queue.Received(1).EnqueueAsync(new BurstDetectionPayload(configId), null, Ct);
    }
}
