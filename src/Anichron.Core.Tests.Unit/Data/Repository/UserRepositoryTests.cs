using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class UserRepositoryTests
{
    private static AnichronDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnichronDbContext(options);
    }

    private static User MakeUser(string username = "alice", string email = "alice@example.com")
        => new()
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = "hash",
        };

    // ==========================================================================
    // GetAllAsync
    // ==========================================================================

    [Fact]
    public async Task GetAllAsync_NoUsers_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new EfUserRepository(db);

        var result = await repo.GetAllAsync(ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_WithUsers_ReturnsAllUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user1 = MakeUser("alice", "alice@example.com");
        var user2 = MakeUser("bob", "bob@example.com");
        await db.AddRangeAsync(user1, user2);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserRepository(db);

        var result = await repo.GetAllAsync(ct);

        result.Should().HaveCount(2);
        result.Should().Contain(u => u.Id == user1.Id);
        result.Should().Contain(u => u.Id == user2.Id);
    }

    [Fact]
    public async Task GetAllAsync_IncludesStorageConfigs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/nas" };
        db.StorageConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserRepository(db);

        var result = await repo.GetAllAsync(ct);

        result.Should().ContainSingle().Which.StorageConfigs.Should().ContainSingle(c => c.Id == config.Id);
    }

    // ==========================================================================
    // FindByIdWithConfigsAsync
    // ==========================================================================

    [Fact]
    public async Task FindByIdWithConfigsAsync_UserExists_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserRepository(db);

        var result = await repo.FindByIdWithConfigsAsync(user.Id, ct);

        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByIdWithConfigsAsync_UserNotFound_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repo = new EfUserRepository(db);

        var result = await repo.FindByIdWithConfigsAsync(Guid.NewGuid(), ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByIdWithConfigsAsync_IncludesStorageConfigs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = user.Id, RootPath = "/nas" };
        db.StorageConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserRepository(db);

        var result = await repo.FindByIdWithConfigsAsync(user.Id, ct);

        result!.StorageConfigs.Should().ContainSingle(c => c.Id == config.Id);
    }

    // ==========================================================================
    // Remove
    // ==========================================================================

    [Fact]
    public async Task Remove_ExistingUser_UserAbsentAfterSave()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var repo = new EfUserRepository(db);

        repo.Remove(user);
        await db.SaveChangesAsync(ct);

        var found = await repo.FindByIdWithConfigsAsync(user.Id, ct);
        found.Should().BeNull();
    }
}
