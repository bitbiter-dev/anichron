namespace Anichron.Worker.Scheduling;

internal static class RecurringWork
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> work,
        TimeSpan interval,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await work(ct);
            await Task.Delay(interval, timeProvider, ct);
        }
    }
}
