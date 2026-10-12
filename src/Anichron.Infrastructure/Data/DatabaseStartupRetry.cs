namespace Anichron.Infrastructure.Data;

public sealed class DatabaseStartupRetry(TimeProvider timeProvider, int maxAttempts, TimeSpan delay)
{
    public async Task RunAsync(
        Func<CancellationToken, Task> operation,
        Action<Exception, int> onRetry,
        Action<Exception> onGiveUp,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await operation(ct);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                onRetry(ex, attempt);
                await Task.Delay(delay, timeProvider, ct);
            }
            catch (Exception ex)
            {
                onGiveUp(ex);
                throw new InvalidOperationException(
                    $"Database unavailable after {maxAttempts} attempts. Aborting.", ex);
            }
        }
    }
}
