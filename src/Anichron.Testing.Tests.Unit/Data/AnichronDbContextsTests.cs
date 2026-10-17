using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace Anichron.Testing.Tests.Unit.Data;

public sealed class AnichronDbContextsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static User NewUser() => new() { Username = "mira", Email = "mira@example.com", PasswordHash = "hash" };

    [Fact]
    public async Task InMemory_WithoutAName_IsADatabaseOfItsOwnAsync()
    {
        await using (var first = AnichronDbContexts.InMemory())
        {
            first.Users.Add(NewUser());
            await first.SaveChangesAsync(Ct);
        }

        await using var second = AnichronDbContexts.InMemory();

        (await second.Users.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task InMemory_SameName_SharesOneDatabaseAsync()
    {
        var name = Guid.NewGuid().ToString();
        await using (var first = AnichronDbContexts.InMemory(name))
        {
            first.Users.Add(NewUser());
            await first.SaveChangesAsync(Ct);
        }

        await using var second = AnichronDbContexts.InMemory(name);

        (await second.Users.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public void InMemoryOptions_ByDefault_CachesTheModel()
        => Caching(AnichronDbContexts.InMemoryOptions()).Should().BeTrue();

    [Fact]
    public void InMemoryOptions_FreshModel_BuildsTheModelAnew()
        => Caching(AnichronDbContexts.InMemoryOptions(freshModel: true)).Should().BeFalse();

    [Fact]
    public void NpgsqlModel_IsNpgsqlWithNodaTimeAndAFreshModel()
    {
        using var db = AnichronDbContexts.NpgsqlModel();

        db.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        db.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(Instant)).Should().NotBeNull();
        Caching(db.GetService<IDbContextOptions>()).Should().BeFalse();
    }

    private static bool Caching(IDbContextOptions options)
        => options.FindExtension<CoreOptionsExtension>()!.ServiceProviderCachingEnabled;
}
