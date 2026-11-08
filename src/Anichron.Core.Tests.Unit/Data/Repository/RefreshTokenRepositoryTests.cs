using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class RefreshTokenRepositoryTests
{
    private static readonly Instant Epoch = Instant.FromUtc(2024, 1, 1, 0, 0);

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
        await using var db = AnichronDbContexts.InMemory();
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
    public async Task FindByHashWithUserAsync_RotatedToken_ReturnsItWithItsSuccessorLoaded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = AnichronDbContexts.InMemory();
        var user = MakeUser();
        db.Users.Add(user);
        var successor = MakeToken(user.Id, "tok2");
        var rotated = MakeToken(user.Id, "tok1", revokedAt: Epoch);
        rotated.ReplacedById = successor.Id;
        db.RefreshTokens.AddRange(successor, rotated);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var result = await new EfRefreshTokenRepository(db).FindByHashWithUserAsync("tok1", ct);

        result.Should().NotBeNull();
        result.ReplacedBy.Should().NotBeNull().And.Subject.As<RefreshToken>().TokenHash.Should().Be("tok2");
    }

    [Fact]
    public async Task FindByHashWithUserAsync_TokenChangedElsewhereSinceThisContextSawIt_ReturnsItAsItIsNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var databaseName = Guid.NewGuid().ToString();
        await using var db = AnichronDbContexts.InMemory(databaseName);
        var user = MakeUser();
        db.Users.Add(user);
        db.RefreshTokens.Add(MakeToken(user.Id, "tok1"));
        await db.SaveChangesAsync(ct);
        await using (var elsewhere = AnichronDbContexts.InMemory(databaseName))
        {
            (await elsewhere.RefreshTokens.SingleAsync(ct)).RevokedAt = Epoch;
            await elsewhere.SaveChangesAsync(ct);
        }

        var result = await new EfRefreshTokenRepository(db).FindByHashWithUserAsync("tok1", ct);

        result.Should().NotBeNull();
        result.RevokedAt.Should().Be(Epoch);
    }

    [Fact]
    public async Task FindByHashWithUserAsync_UnknownHash_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = AnichronDbContexts.InMemory();

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
        await using var db = AnichronDbContexts.InMemory();
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
        await using var db = AnichronDbContexts.InMemory();

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
        await using var db = AnichronDbContexts.InMemory();
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
