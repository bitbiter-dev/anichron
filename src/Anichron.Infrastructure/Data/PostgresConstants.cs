namespace Anichron.Infrastructure.Data;

public static class PostgresConstants
{
    /// <summary>
    /// Advisory lock key used to coordinate database migrations across all services.
    /// Any service that calls <c>MigrateWithAdvisoryLockAsync</c> competes for this lock,
    /// ensuring only one process applies migrations at a time.
    /// </summary>
    public const long MigrationAdvisoryLockId = 7_274_958_328L;
}
