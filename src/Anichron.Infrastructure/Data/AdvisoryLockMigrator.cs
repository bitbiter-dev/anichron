namespace Anichron.Infrastructure.Data;

public interface IAdvisoryLock
{
    Task<bool> TryAcquireAsync(CancellationToken ct);
    Task ReleaseAsync(CancellationToken ct);
}

public sealed class AdvisoryLockMigrator(IAdvisoryLock advisoryLock, TimeProvider timeProvider, int maxAttempts)
{
    private static readonly TimeSpan pollInterval = TimeSpan.FromSeconds(1);

    public async Task RunAsync(Func<CancellationToken, Task> migrate, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (await advisoryLock.TryAcquireAsync(ct))
            {
                try
                {
                    await migrate(ct);
                    return;
                }
                finally
                {
                    await advisoryLock.ReleaseAsync(ct);
                }
            }

            if (attempt < maxAttempts)
                await Task.Delay(pollInterval, timeProvider, ct);
        }

        throw new TimeoutException(
            $"Could not acquire the migration advisory lock after {maxAttempts} attempts.");
    }
}
