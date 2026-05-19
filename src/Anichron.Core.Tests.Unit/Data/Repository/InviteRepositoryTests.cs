using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class InviteRepositoryTests
{
    private static readonly Instant Epoch = Instant.FromUtc(2024, 1, 1, 0, 0);

    private static AnichronDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnichronDbContext(options);
    }

    private static Invite MakeInvite(string hash = "tok", Instant? expiresAt = null, Instant? usedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TokenHash = hash,
            CreatedAt = Epoch,
            ExpiresAt = expiresAt ?? (Epoch + Duration.FromDays(7)),
            UsedAt = usedAt,
        };

    // ==========================================================================
    // FindValidByHashAsync
    // ==========================================================================

    [Fact]
    public async Task FindValidByHashAsync_ValidToken_ReturnsInvite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var invite = MakeInvite("validhash");
        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);

        var result = await new EfInviteRepository(db).FindValidByHashAsync("validhash", Epoch, ct);

        result.Should().NotBeNull();
        result.Id.Should().Be(invite.Id);
    }

    [Fact]
    public async Task FindValidByHashAsync_ExpiredToken_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Invites.Add(MakeInvite("expiredhash", expiresAt: Epoch - Duration.FromSeconds(1)));
        await db.SaveChangesAsync(ct);

        var result = await new EfInviteRepository(db).FindValidByHashAsync("expiredhash", Epoch, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindValidByHashAsync_AlreadyUsedToken_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        db.Invites.Add(MakeInvite("usedhash", usedAt: Epoch - Duration.FromDays(1)));
        await db.SaveChangesAsync(ct);

        var result = await new EfInviteRepository(db).FindValidByHashAsync("usedhash", Epoch, ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindValidByHashAsync_UnknownHash_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();

        var result = await new EfInviteRepository(db).FindValidByHashAsync("nope", Epoch, ct);

        result.Should().BeNull();
    }

    // ==========================================================================
    // Add
    // ==========================================================================

    [Fact]
    public async Task Add_AddsInviteToContext()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateDb();
        var invite = MakeInvite("newinvite");
        var repository = new EfInviteRepository(db);

        repository.Add(invite);
        await db.SaveChangesAsync(ct);

        var found = await repository.FindValidByHashAsync("newinvite", Epoch, ct);
        found.Should().NotBeNull();
    }
}
