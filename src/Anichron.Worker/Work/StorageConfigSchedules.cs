using Anichron.Core.Data.Repository;
using Anichron.Core.Work;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Anichron.Worker.Work;

/// <summary>One schedule per active storage config the Worker serves, at the crawl interval.</summary>
/// <typeparam name="TPayload">The per-config kind scheduled.</typeparam>
/// <param name="workerState">Names the user a per-user Worker serves.</param>
/// <param name="options">Supplies the crawl interval.</param>
public sealed class StorageConfigSchedules<TPayload>(WorkerState workerState, IOptions<WorkerSettings> options)
    : IWorkScheduleSource
    where TPayload : IStorageConfigPayload<TPayload>
{
    public async Task<IReadOnlyList<IWorkSchedule>> SchedulesAsync(IServiceProvider services, CancellationToken ct)
    {
        var repository = services.GetRequiredService<IUserStorageConfigRepository>();
        var served = workerState.ResolvedUserId is { } userId
            ? await repository.GetActiveByUserIdAsync(userId, ct)
            : await repository.GetAllActiveAsync(ct);
        var interval = Duration.FromHours(options.Value.CrawlIntervalHours);
        return [.. served.Select(config => new WorkSchedule<TPayload>(TPayload.For(config.Id), interval))];
    }
}
