using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anichron.Core.Data;

internal sealed class AnichronDbContextFactory : IDesignTimeDbContextFactory<AnichronDbContext>
{
    public AnichronDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseAnichronNpgsql("Host=localhost;Database=anichron_design;Username=postgres")
            .Options;
        return new AnichronDbContext(options);
    }
}
