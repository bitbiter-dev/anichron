using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Anichron.Tests.Integration.PostgresServer))]

namespace Anichron.Tests.Integration;

public sealed class PostgresServer : IAsyncLifetime
{
    public const string Image = "postgres:17-alpine";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image)
        .WithCommand("-c", "max_connections=500")
        .Build();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync(CancellationToken ct)
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync(ct);
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public async Task<string> CreateMigratedDatabaseAsync(CancellationToken ct)
    {
        var connectionString = await CreateDatabaseAsync(ct);
        await using var db = Context(connectionString);
        await db.Database.MigrateAsync(ct);
        return connectionString;
    }

    public static AnichronDbContext Context(string connectionString)
        => new(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql(connectionString, o => o.UseNodaTime())
            .Options);
}
