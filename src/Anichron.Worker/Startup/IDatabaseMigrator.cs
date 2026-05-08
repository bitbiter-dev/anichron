using Anichron.Core.Data;
using Anichron.Infrastructure.Data;

namespace Anichron.Worker.Startup;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken ct);
}

public sealed class EfDatabaseMigrator(AnichronDbContext db) : IDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken ct)
        => db.Database.MigrateWithAdvisoryLockAsync(ct);
}
