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
