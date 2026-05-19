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
        var repository = new EfUserRepository(db);

        var result = await repository.GetAllAsync(ct);

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
        var repository = new EfUserRepository(db);

        var result = await repository.GetAllAsync(ct);

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
        var repository = new EfUserRepository(db);

        var result = await repository.GetAllAsync(ct);

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
        var repository = new EfUserRepository(db);

        var result = await repository.FindByIdWithConfigsAsync(user.Id, ct);

        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByIdWithConfigsAsync_UserNotFound_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var repository = new EfUserRepository(db);

        var result = await repository.FindByIdWithConfigsAsync(Guid.NewGuid(), ct);

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
        var repository = new EfUserRepository(db);

        var result = await repository.FindByIdWithConfigsAsync(user.Id, ct);

        result!.StorageConfigs.Should().ContainSingle(c => c.Id == config.Id);
    }

    // ==========================================================================
    // AnyAsync
    // ==========================================================================

    [Fact]
    public async Task AnyAsync_NoUsers_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var result = await new EfUserRepository(db).AnyAsync(ct);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnyAsync_UsersExist_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Users.Add(MakeUser());
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).AnyAsync(ct);
        result.Should().BeTrue();
    }

    // ==========================================================================
    // AnyByUsernameAsync
    // ==========================================================================

    [Fact]
    public async Task AnyByUsernameAsync_UsernameExists_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Users.Add(MakeUser("alice", "alice@example.com"));
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).AnyByUsernameAsync("alice", ct);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AnyByUsernameAsync_UsernameDoesNotExist_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var result = await new EfUserRepository(db).AnyByUsernameAsync("nobody", ct);
        result.Should().BeFalse();
    }

    // ==========================================================================
    // AnyByEmailAsync
    // ==========================================================================

    [Fact]
    public async Task AnyByEmailAsync_EmailExists_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Users.Add(MakeUser("alice", "alice@example.com"));
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).AnyByEmailAsync("alice@example.com", ct);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AnyByEmailAsync_EmailDoesNotExist_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var result = await new EfUserRepository(db).AnyByEmailAsync("nobody@example.com", ct);
        result.Should().BeFalse();
    }

    // ==========================================================================
    // FindByIdAsync
    // ==========================================================================

    [Fact]
    public async Task FindByIdAsync_ExistingId_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindByIdAsync(user.Id, ct);
        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByIdAsync_UnknownId_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var result = await new EfUserRepository(db).FindByIdAsync(Guid.NewGuid(), ct);
        result.Should().BeNull();
    }

    // ==========================================================================
    // FindByCredentialAsync
    // ==========================================================================

    [Fact]
    public async Task FindByCredentialAsync_ByUsername_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser("alice", "alice@example.com");
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindByCredentialAsync("alice", ct);
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByCredentialAsync_ByEmail_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser("alice", "alice@example.com");
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindByCredentialAsync("alice@example.com", ct);
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByCredentialAsync_NoMatch_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var result = await new EfUserRepository(db).FindByCredentialAsync("nobody", ct);
        result.Should().BeNull();
    }

    // ==========================================================================
    // FindAdminByUsernameAsync
    // ==========================================================================

    [Fact]
    public async Task FindAdminByUsernameAsync_AdminExists_ReturnsUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var admin = MakeUser("admin", "admin@example.com");
        admin.IsAdmin = true;
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindAdminByUsernameAsync("admin", ct);
        result!.Id.Should().Be(admin.Id);
    }

    [Fact]
    public async Task FindAdminByUsernameAsync_NonAdminWithUsername_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Users.Add(MakeUser("alice", "alice@example.com"));
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindAdminByUsernameAsync("alice", ct);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindAdminByUsernameAsync_NoMatch_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var result = await new EfUserRepository(db).FindAdminByUsernameAsync("nobody", ct);
        result.Should().BeNull();
    }

    // ==========================================================================
    // FindAdminsAsync
    // ==========================================================================

    [Fact]
    public async Task FindAdminsAsync_ReturnsOnlyAdmins()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var admin = MakeUser("admin", "admin@example.com");
        admin.IsAdmin = true;
        var regular = MakeUser("alice", "alice@example.com");
        await db.Users.AddRangeAsync(admin, regular);
        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindAdminsAsync(10, ct);
        result.Should().ContainSingle(u => u.Id == admin.Id);
    }

    [Fact]
    public async Task FindAdminsAsync_RespectsTakeLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        for (var i = 0; i < 5; i++)
        {
            var admin = MakeUser($"admin{i}", $"admin{i}@example.com");
            admin.IsAdmin = true;
            db.Users.Add(admin);
        }

        await db.SaveChangesAsync(ct);
        var result = await new EfUserRepository(db).FindAdminsAsync(3, ct);
        result.Should().HaveCount(3);
    }

    // ==========================================================================
    // Add
    // ==========================================================================

    [Fact]
    public async Task Add_AddsUserToContext()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        var repository = new EfUserRepository(db);
        repository.Add(user);
        await db.SaveChangesAsync(ct);
        var found = await repository.FindByIdAsync(user.Id, ct);
        found.Should().NotBeNull();
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
        var repository = new EfUserRepository(db);

        repository.Remove(user);
        await db.SaveChangesAsync(ct);

        var found = await repository.FindByIdWithConfigsAsync(user.Id, ct);
        found.Should().BeNull();
    }
}
