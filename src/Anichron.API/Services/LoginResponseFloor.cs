namespace Anichron.API.Services;

public interface ILoginResponseFloor
{
    long Start();
    Task PadAsync(long startedAt, CancellationToken ct);
}

public sealed class LoginResponseFloor(TimeProvider timeProvider, TimeSpan floor) : ILoginResponseFloor
{
    public long Start() => timeProvider.GetTimestamp();

    public Task PadAsync(long startedAt, CancellationToken ct)
    {
        var remaining = floor - timeProvider.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero
            ? Task.Delay(remaining, timeProvider, ct)
            : Task.CompletedTask;
    }
}
