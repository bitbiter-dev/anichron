using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Data;

public sealed class BurstDeleteBehaviourTests(PostgresServer server)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 23, 9, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Library(string ConnectionString, Guid ConfigId, Guid BurstId, Guid CoverId, Guid OtherId);

    private async Task<Library> LibraryAsync()
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        var (user, config, burst) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        MediaAsset Photo(string name) => new()
        {
            Id = Guid.NewGuid(),
            StorageConfigId = config,
            BurstId = burst,
            FilePath = name,
            FileName = name,
            ContentHash = name,
            MediaType = MediaType.Image,
        };
        var (cover, other) = (Photo("a.heic"), Photo("b.heic"));

        await using var db = PostgresServer.Context(connectionString);
        db.Users.Add(new User { Id = user, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.Add(new UserStorageConfig { Id = config, UserId = user, RootPath = "/data/originals/mira", IsActive = true });
        db.Bursts.Add(new Burst { Id = burst, CreatedAt = Now });
        db.MediaAssets.AddRange(cover, other);
        db.BurstChecks.AddRange(
            new BurstCheck { AssetId = cover.Id, CheckedAt = Now, IsCandidate = true, CameraBurstId = "label" },
            new BurstCheck { AssetId = other.Id, CheckedAt = Now, IsCandidate = true, CameraBurstId = "label" });
        await db.SaveChangesAsync(Ct);
        await db.Bursts.Where(b => b.Id == burst).ExecuteUpdateAsync(s => s.SetProperty(b => b.PrimaryAssetId, cover.Id), Ct);
        return new Library(connectionString, config, burst, cover.Id, other.Id);
    }

    [Fact]
    public async Task DeletingTheStorageConfig_RemovesItsAssetsAndChecksAndLeavesTheBurstCoverlessAsync()
    {
        var library = await LibraryAsync();
        await using var db = PostgresServer.Context(library.ConnectionString);

        await db.StorageConfigs.Where(c => c.Id == library.ConfigId).ExecuteDeleteAsync(Ct);

        (await db.MediaAssets.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.BurstChecks.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await db.Bursts.IgnoreQueryFilters().SingleAsync(Ct)).Should().BeEquivalentTo(
            new { Id = library.BurstId, PrimaryAssetId = (Guid?)null });
    }

    [Fact]
    public async Task DeletingTheCover_ClearsItButKeepsTheOtherMemberAsync()
    {
        var library = await LibraryAsync();
        await using var db = PostgresServer.Context(library.ConnectionString);

        await db.MediaAssets.Where(a => a.Id == library.CoverId).ExecuteDeleteAsync(Ct);

        (await db.Bursts.SingleAsync(Ct)).PrimaryAssetId.Should().BeNull();
        (await db.MediaAssets.SingleAsync(Ct)).Should().BeEquivalentTo(new { Id = library.OtherId, BurstId = (Guid?)library.BurstId });
    }
}
