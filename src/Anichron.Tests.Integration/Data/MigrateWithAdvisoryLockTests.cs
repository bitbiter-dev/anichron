using Anichron.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Tests.Integration.Data;

public sealed class MigrateWithAdvisoryLockTests(PostgresServer server)
{
    [Fact]
    public async Task MigrateWithAdvisoryLockAsync_TwoConcurrentMigrators_BothCompleteAndApplyEachMigrationOnceAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateDatabaseAsync(ct);
        await using var first = PostgresServer.Context(connectionString);
        await using var second = PostgresServer.Context(connectionString);

        await Task.WhenAll(
            first.Database.MigrateWithAdvisoryLockAsync(ct),
            second.Database.MigrateWithAdvisoryLockAsync(ct));

        await using var check = PostgresServer.Context(connectionString);
        var history = await check.Database
            .SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToListAsync(ct);
        history.Should().NotBeEmpty().And.Equal(check.Database.GetMigrations());
    }

    [Fact]
    public async Task MigrateWithAdvisoryLockAsync_Finished_ReleasesTheLockAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateDatabaseAsync(ct);
        await using (var db = PostgresServer.Context(connectionString))
            await db.Database.MigrateWithAdvisoryLockAsync(ct);

        await using var other = await OpenAsync(connectionString, ct);

        (await TryLockAsync(other, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateWithAdvisoryLockAsync_LockHeldElsewhere_GivesUpWithoutMigratingAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateDatabaseAsync(ct);
        await using var holder = await OpenAsync(connectionString, ct);
        (await TryLockAsync(holder, ct)).Should().BeTrue();

        await using var db = PostgresServer.Context(connectionString);
        var migrate = () => db.Database.MigrateWithAdvisoryLockAsync(ct, maxAttempts: 1);

        await migrate.Should().ThrowAsync<TimeoutException>();
        (await db.Database.GetAppliedMigrationsAsync(ct)).Should().BeEmpty();
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@id)", connection);
        command.Parameters.AddWithValue("id", PostgresConstants.MigrationAdvisoryLockId);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }
}
