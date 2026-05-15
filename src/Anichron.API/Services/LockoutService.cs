using Anichron.API.Settings;
using Anichron.Core.Data;
using Anichron.Core.Domain;

namespace Anichron.API.Services;

public interface ILockoutService
{
    bool IsLockedOut(User user, Instant now);
    Task RecordFailedAttemptAsync(User user, Instant now, CancellationToken ct);
    // Mutates the entity only — the caller is responsible for persisting inside a transaction.
    void PrepareReset(User user);
}

internal sealed class LockoutService(IUnitOfWork unitOfWork) : ILockoutService
{
    public bool IsLockedOut(User user, Instant now)
        => user.LockedUntil is { } lockedUntil && lockedUntil > now;

    public async Task RecordFailedAttemptAsync(User user, Instant now, CancellationToken ct)
    {
        user.FailedLoginAttempts++;
        user.LockedUntil = now.Plus(Duration.FromSeconds(ComputeBackoffSeconds(user.FailedLoginAttempts)));
        await unitOfWork.SaveChangesAsync(ct);
    }

    public void PrepareReset(User user)
    {
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
    }

    internal static int ComputeBackoffSeconds(int failedAttempts) => failedAttempts switch
    {
        <= AppDefaults.Lockout.AllowedAttempts => 0,
        >= AppDefaults.Lockout.MaxAttempts => AppDefaults.Lockout.MaxSeconds,
        _ => (int)Math.Pow(AppDefaults.Lockout.BackoffBase, failedAttempts - AppDefaults.Lockout.AllowedAttempts),
    };
}
