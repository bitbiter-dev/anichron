using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace Anichron.Infrastructure.Data;

public static class DatabaseFacadeExtensions
{
    // S2077 flags the two interpolated pg_advisory_lock command texts below as SQL built by
    // string formatting. There is no injection surface: the only interpolated value is
    // PostgresConstants.MigrationAdvisoryLockId, a `const long` fixed at compile time, and no
    // caller can influence it. The rule matches the interpolation PATTERN, not a reachable taint
    // path, so this is a false positive rather than a finding to fix.
    //
    // Suppressed at the method rather than in .editorconfig so the justification travels with the
    // code a reader is looking at. It became an ERROR rather than a warning when
    // SonarAnalyzer.CSharp went 10.25 → 10.34 under TreatWarningsAsErrors.
    //
    // 📌 Parameterising both commands would remove the suppression and is worth doing on its own
    // merits — but it is a behaviour change, and this is a dependency-bump PR.
    [SuppressMessage(
        "Major Code Smell",
        "S2077:Formatting SQL queries is security-sensitive",
        Justification = "Interpolates only a compile-time const; no caller-controlled input reaches this SQL.")]
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
