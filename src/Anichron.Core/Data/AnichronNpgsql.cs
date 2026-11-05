using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data;

public static class AnichronNpgsql
{
    extension<TBuilder>(TBuilder builder) where TBuilder : DbContextOptionsBuilder
    {
        public TBuilder UseAnichronNpgsql(string connectionString)
        {
            builder.UseNpgsql(connectionString, o => o.UseNodaTime());
            return builder;
        }
    }
}
