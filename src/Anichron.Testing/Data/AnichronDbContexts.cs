using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anichron.Testing.Data;

public static class AnichronDbContexts
{
    public static DbContextOptions<AnichronDbContext> InMemoryOptions()
        => new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseInternalServiceProvider(new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider())
            .Options;

    public static AnichronDbContext InMemory() => new(InMemoryOptions());

    public static AnichronDbContext NpgsqlModel()
        => new(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql("Host=unused", o => o.UseNodaTime())
            .EnableServiceProviderCaching(false)
            .Options);
}
