using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class RefreshTokenRepositoryTests
{
    private static readonly Instant Epoch = Instant.FromUtc(2024, 1, 1, 0, 0);

    private static AnichronDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnichronDbContext(options);
    }

    private static User MakeUser(string username = "alice")
        => new() { Id = Guid.NewGuid(), Username = username, Email = $"{username}@example.com", PasswordHash = "hash" };

    private static RefreshToken MakeToken(Guid userId, string hash = "abc", Instant? revokedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            CreatedAt = Epoch,
            ExpiresAt = Epoch + Duration.FromDays(30),
            RevokedAt = revokedAt,
        };

    // ==========================================================================
    // FindByHashWithUserAsync
    // ==========================================================================

    [Fact]
    public async Task FindByHashWithUserAsync_KnownHash_ReturnsTokenWithUserLoaded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        db.RefreshTokens.Add(MakeToken(user.Id, "tok1"));
        await db.SaveChangesAsync(ct);

        var result = await new EfRefreshTokenRepository(db).FindByHashWithUserAsync("tok1", ct);

        result.Should().NotBeNull();
        result.User.Should().NotBeNull();
        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task FindByHashWithUserAsync_UnknownHash_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await new EfRefreshTokenRepository(db).FindByHashWithUserAsync("nope", ct);

        result.Should().BeNull();
    }

    // ==========================================================================
    // FindByHashAsync
    // ==========================================================================

    [Fact]
    public async Task FindByHashAsync_KnownHash_ReturnsToken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var token = MakeToken(user.Id, "tok2");
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        var result = await new EfRefreshTokenRepository(db).FindByHashAsync("tok2", ct);

        result.Should().NotBeNull();
        result.Id.Should().Be(token.Id);
    }

    [Fact]
    public async Task FindByHashAsync_UnknownHash_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await new EfRefreshTokenRepository(db).FindByHashAsync("nope", ct);

        result.Should().BeNull();
    }

    // ==========================================================================
    // Add
    // ==========================================================================

    [Fact]
    public async Task Add_AddsTokenToContext()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var user = MakeUser();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var token = MakeToken(user.Id, "tok3");
        var repository = new EfRefreshTokenRepository(db);

        repository.Add(token);
        await db.SaveChangesAsync(ct);

        var found = await repository.FindByHashAsync("tok3", ct);
        found.Should().NotBeNull();
    }
}
