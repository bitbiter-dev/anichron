using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Anichron.Infrastructure.Configuration;

public static class DatabaseFacadeExtensions
{
    public static async Task MigrateWithAdvisoryLockAsync(
        this DatabaseFacade database, CancellationToken ct, int maxAttempts = 30)
    {
        // Explicitly hold the connection open so that all operations — acquire lock,
        // migrate, release lock — run on the same PostgreSQL session. Session-level advisory
        // locks are tied to the session; a different connection would see a different lock.
        await database.OpenConnectionAsync(ct);
        try
        {
            var conn = database.GetDbConnection();

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                bool acquired;
                await using (var tryLockCmd = conn.CreateCommand())
                {
                    tryLockCmd.CommandText =
                        $"SELECT pg_try_advisory_lock({PostgresConstants.MigrationAdvisoryLockId})";
                    acquired = (bool)(await tryLockCmd.ExecuteScalarAsync(ct))!;
                }

                if (acquired)
                {
                    try
                    {
                        await database.MigrateAsync(ct);
                        return;
                    }
                    finally
                    {
                        await using var unlockCmd = conn.CreateCommand();
                        unlockCmd.CommandText =
                            $"SELECT pg_advisory_unlock({PostgresConstants.MigrationAdvisoryLockId})";
                        await unlockCmd.ExecuteNonQueryAsync(ct);
                    }
                }

                if (attempt < maxAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }

            throw new TimeoutException(
                $"Could not acquire the migration advisory lock after {maxAttempts} attempts.");
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }
}
