using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Data;

public sealed class RefreshTokenRepositoryTests(PostgresServer server)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 18, 12, 0);

    [Fact]
    public async Task DeleteExpiredAsync_DeletesOnlyTokensExpiredBeforeTheCutoff_AndCountsThemAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateMigratedDatabaseAsync(ct);
        await SeedAsync(connectionString, ct,
            ("long-gone", Now - Duration.FromDays(30)),
            ("just-expired", Now - Duration.FromMilliseconds(1)),
            ("expires-now", Now),
            ("still-valid", Now + Duration.FromDays(7)));

        await using (var db = PostgresServer.Context(connectionString))
        {
            var deleted = await new EfRefreshTokenRepository(db).DeleteExpiredAsync(Now, ct);

            deleted.Should().Be(2);
        }

        await using var check = PostgresServer.Context(connectionString);
        (await check.RefreshTokens.Select(t => t.TokenHash).ToListAsync(ct))
            .Should().BeEquivalentTo("expires-now", "still-valid");
    }

    [Fact]
    public async Task DeleteExpiredAsync_NothingExpired_DeletesNothingAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateMigratedDatabaseAsync(ct);
        await SeedAsync(connectionString, ct, ("still-valid", Now + Duration.FromDays(7)));

        await using var db = PostgresServer.Context(connectionString);
        var deleted = await new EfRefreshTokenRepository(db).DeleteExpiredAsync(Now, ct);

        deleted.Should().Be(0);
        (await db.RefreshTokens.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task RevokeAllActiveByUserIdAsync_InTheTransactionThatSavesANewToken_RevokesOnlyTheTokensAlreadySavedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateMigratedDatabaseAsync(ct);
        await SeedAsync(connectionString, ct, ("other-device", Now + Duration.FromDays(7)));

        await using (var db = PostgresServer.Context(connectionString))
        {
            var userId = await db.Users.Select(u => u.Id).SingleAsync(ct);
            var repository = new EfRefreshTokenRepository(db);
            repository.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = "this-device",
                CreatedAt = Now,
                ExpiresAt = Now + Duration.FromDays(7),
            });

            await db.ExecuteInTransactionAsync(async () =>
            {
                await repository.RevokeAllActiveByUserIdAsync(userId, Now, ct);
                await db.SaveChangesAsync(ct);
            }, ct);
        }

        await using var check = PostgresServer.Context(connectionString);
        (await check.RefreshTokens.ToDictionaryAsync(t => t.TokenHash, t => t.RevokedAt, ct))
            .Should().Equal(new Dictionary<string, Instant?> { ["other-device"] = Now, ["this-device"] = null });
    }

    [Fact]
    public async Task TryRotateAsync_TwoRotationsOfTheSameTokenAtOnce_OnlyOneRotatesItAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateMigratedDatabaseAsync(ct);
        await SeedAsync(connectionString, ct, ("presented", Now + Duration.FromDays(7)));
        RefreshToken presented;
        await using (var db = PostgresServer.Context(connectionString))
            presented = await db.RefreshTokens.SingleAsync(ct);

        await using var first = PostgresServer.Context(connectionString);
        await using var second = PostgresServer.Context(connectionString);
        var rotations = await Task.WhenAll(
            new EfRefreshTokenRepository(first).TryRotateAsync(presented.Id, Successor(presented.UserId, "first"), ct),
            new EfRefreshTokenRepository(second).TryRotateAsync(presented.Id, Successor(presented.UserId, "second"), ct));

        rotations.Should().ContainSingle(rotation => rotation == TokenRotation.Rotated);
        var winner = rotations[0] == TokenRotation.Rotated ? "first" : "second";
        await using var check = PostgresServer.Context(connectionString);
        var stored = await check.RefreshTokens.Include(t => t.ReplacedBy).ToListAsync(ct);
        Assert.Multiple(() =>
        {
            stored.Select(t => t.TokenHash).Should().BeEquivalentTo("presented", winner);
            var rotated = stored.Should().ContainSingle(t => t.TokenHash == "presented").Which;
            rotated.ReplacedBy.Should().NotBeNull().And.Subject.As<RefreshToken>().TokenHash.Should().Be(winner);
            rotated.RevokedAt.Should().Be(Now);
        });
    }

    [Fact]
    public async Task TryRotateAsync_RevokedToken_LeavesItAndStoresNoSuccessorAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await server.CreateMigratedDatabaseAsync(ct);
        await SeedAsync(connectionString, ct, ("revoked", Now + Duration.FromDays(7)));
        await using (var db = PostgresServer.Context(connectionString))
        {
            await db.RefreshTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now - Duration.FromMinutes(1)), ct);
        }

        await using (var db = PostgresServer.Context(connectionString))
        {
            var token = await db.RefreshTokens.SingleAsync(ct);
            var rotated = await new EfRefreshTokenRepository(db).TryRotateAsync(token.Id, Successor(token.UserId, "late"), ct);

            rotated.Should().Be(TokenRotation.Lost);
        }

        await using var check = PostgresServer.Context(connectionString);
        var stored = await check.RefreshTokens.SingleAsync(ct);
        Assert.Multiple(() =>
        {
            stored.TokenHash.Should().Be("revoked");
            stored.RevokedAt.Should().Be(Now - Duration.FromMinutes(1));
            stored.ReplacedById.Should().BeNull();
        });
    }

    private static RefreshToken Successor(Guid userId, string hash) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = hash,
        CreatedAt = Now,
        ExpiresAt = Now + Duration.FromDays(30),
    };

    private static async Task SeedAsync(
        string connectionString, CancellationToken ct, params (string Hash, Instant ExpiresAt)[] tokens)
    {
        await using var db = PostgresServer.Context(connectionString);
        var user = new User { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com", PasswordHash = "hash" };
        db.Users.Add(user);
        foreach (var (hash, expiresAt) in tokens)
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = hash,
                CreatedAt = expiresAt - Duration.FromDays(14),
                ExpiresAt = expiresAt,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
