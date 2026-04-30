using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anichron.Core.Data;

internal sealed class AnichronDbContextFactory : IDesignTimeDbContextFactory<AnichronDbContext>
{
    public AnichronDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql("Host=localhost;Database=anichron_design;Username=postgres",
                o => o.UseNodaTime())
            .Options;
        return new AnichronDbContext(options);
    }
}
