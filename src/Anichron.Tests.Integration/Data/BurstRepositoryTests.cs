using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Data;

public sealed class BurstRepositoryTests(PostgresServer server)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 24, 9, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Library(string ConnectionString, Guid Kept, Guid KeptBySoftDeletedMember, Guid Memberless, Guid AlsoMemberless);

    private async Task<Library> LibraryAsync()
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        var (user, config) = (Guid.NewGuid(), Guid.NewGuid());
        var (kept, keptBySoftDeleted, memberless, alsoMemberless) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        MediaAsset Photo(string name, Guid burst, bool softDeleted = false) => new()
        {
            Id = Guid.NewGuid(),
            StorageConfigId = config,
            BurstId = burst,
            FilePath = name,
            FileName = name,
            ContentHash = name,
            MediaType = MediaType.Image,
            IsSoftDeleted = softDeleted,
        };

        await using var db = PostgresServer.Context(connectionString);
        db.Users.Add(new User { Id = user, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.Add(new UserStorageConfig { Id = config, UserId = user, RootPath = "/data/originals/mira", IsActive = true });
        db.Bursts.AddRange(
            new Burst { Id = kept, CreatedAt = Now },
            new Burst { Id = keptBySoftDeleted, CreatedAt = Now },
            new Burst { Id = memberless, CreatedAt = Now },
            new Burst { Id = alsoMemberless, CreatedAt = Now });
        db.MediaAssets.AddRange(Photo("a.heic", kept), Photo("b.heic", keptBySoftDeleted, softDeleted: true));
        await db.SaveChangesAsync(Ct);
        return new Library(connectionString, kept, keptBySoftDeleted, memberless, alsoMemberless);
    }

    [Fact]
    public async Task DeleteMemberlessAsync_DeletesOnlyBurstsWithoutAnyMemberRowAsync()
    {
        var library = await LibraryAsync();
        await using var db = PostgresServer.Context(library.ConnectionString);

        var deleted = await new EfBurstRepository(db).DeleteMemberlessAsync(Ct);

        deleted.Should().Be(2);
        (await db.Bursts.IgnoreQueryFilters().Select(b => b.Id).ToListAsync(Ct))
            .Should().BeEquivalentTo([library.Kept, library.KeptBySoftDeletedMember]);
    }

    [Fact]
    public async Task DeleteMemberlessAsync_TwoWorkersAtOnce_NeitherFailsAndEachBurstIsDeletedOnceAsync()
    {
        var library = await LibraryAsync();
        await using var first = PostgresServer.Context(library.ConnectionString);
        await using var second = PostgresServer.Context(library.ConnectionString);

        var deleted = await Task.WhenAll(
            new EfBurstRepository(first).DeleteMemberlessAsync(Ct),
            new EfBurstRepository(second).DeleteMemberlessAsync(Ct));

        deleted.Sum().Should().Be(2);
        (await first.Bursts.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(2);
    }
}
