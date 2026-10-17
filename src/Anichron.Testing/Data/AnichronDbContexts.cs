using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Testing.Data;

public static class AnichronDbContexts
{
    public static DbContextOptions<AnichronDbContext> InMemoryOptions(string? databaseName = null, bool freshModel = false)
    {
        var builder = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());
        if (freshModel)
            builder.EnableServiceProviderCaching(false);
        return builder.Options;
    }

    public static AnichronDbContext InMemory(string? databaseName = null, bool freshModel = false)
        => new(InMemoryOptions(databaseName, freshModel));

    public static AnichronDbContext NpgsqlModel()
        => new(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql("Host=unused", o => o.UseNodaTime())
            .EnableServiceProviderCaching(false)
            .Options);
}
