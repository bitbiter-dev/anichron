using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class UserStorageConfigRepositoryTests
{
    private static AnichronDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnichronDbContext(options);
    }

    private static UserStorageConfig MakeConfig(Guid userId, string rootPath, bool isActive = true)
        => new() { Id = Guid.NewGuid(), UserId = userId, RootPath = rootPath, IsActive = isActive };

    // ==========================================================================
    // FindByRootPathAsync
    // ==========================================================================

    [Fact]
    public async Task FindByRootPathAsync_MatchingRootPath_ReturnsConfig()
    {
        // Note: InMemory string comparison is case-insensitive; real Postgres text is case-sensitive.
        // The exact-case match tested here is correct for both providers.
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var config = MakeConfig(Guid.NewGuid(), "/nas/photos");
        db.StorageConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.FindByRootPathAsync("/nas/photos", ct);

        result.Should().NotBeNull();
        result.Id.Should().Be(config.Id);
    }

    [Fact]
    public async Task FindByRootPathAsync_NoMatch_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.StorageConfigs.Add(MakeConfig(Guid.NewGuid(), "/nas/photos"));
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.FindByRootPathAsync("/nas/videos", ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByRootPathAsync_PartialPath_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.StorageConfigs.Add(MakeConfig(Guid.NewGuid(), "/nas/photos"));
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.FindByRootPathAsync("/nas", ct);

        result.Should().BeNull();
    }

    // ==========================================================================
    // GetAllActiveAsync
    // ==========================================================================

    [Fact]
    public async Task GetAllActiveAsync_MixedActive_ReturnsOnlyActive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var active1 = MakeConfig(userId, "/a", isActive: true);
        var active2 = MakeConfig(userId, "/b", isActive: true);
        var inactive = MakeConfig(userId, "/c", isActive: false);
        await db.AddRangeAsync(active1, active2, inactive);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.GetAllActiveAsync(ct);

        result.Should().HaveCount(2);
        result.Should().NotContain(c => c.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAllActiveAsync_NoneActive_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.StorageConfigs.Add(MakeConfig(Guid.NewGuid(), "/a", isActive: false));
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.GetAllActiveAsync(ct);

        result.Should().BeEmpty();
    }

    // ==========================================================================
    // GetActiveByUserIdAsync
    // ==========================================================================

    [Fact]
    public async Task GetActiveByUserIdAsync_MatchingUserId_ReturnsOnlyThatUsersConfigs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var mine = MakeConfig(userId, "/mine", isActive: true);
        var theirs = MakeConfig(otherUserId, "/theirs", isActive: true);
        await db.AddRangeAsync(mine, theirs);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.GetActiveByUserIdAsync(userId, ct);

        result.Should().ContainSingle(c => c.Id == mine.Id);
        result.Should().NotContain(c => c.Id == theirs.Id);
    }

    [Fact]
    public async Task GetActiveByUserIdAsync_InactiveConfigForUser_Excluded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.StorageConfigs.Add(MakeConfig(userId, "/archive", isActive: false));
        await db.SaveChangesAsync(ct);
        var repo = new EfUserStorageConfigRepository(db);

        var result = await repo.GetActiveByUserIdAsync(userId, ct);

        result.Should().BeEmpty();
    }

    // ==========================================================================
    // Add
    // ==========================================================================

    [Fact]
    public async Task Add_Config_AppearsInSubsequentQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new EfUserStorageConfigRepository(db);
        var config = MakeConfig(Guid.NewGuid(), "/new/path");

        repo.Add(config);
        await db.SaveChangesAsync(ct);

        var found = await repo.FindByRootPathAsync("/new/path", ct);
        found.Should().NotBeNull();
        found.Id.Should().Be(config.Id);
    }
}
