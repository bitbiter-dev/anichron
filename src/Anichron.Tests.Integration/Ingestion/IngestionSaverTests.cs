using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Ingestion;

public sealed class IngestionSaverTests(PostgresServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Moved = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid AtPath = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid Other = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private async Task<string> LibraryAsync()
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        var (user, config) = (Guid.NewGuid(), Guid.NewGuid());
        MediaAsset Photo(Guid id, string path, string hash) => new()
        {
            Id = id,
            StorageConfigId = config,
            FilePath = path,
            FileName = path,
            ContentHash = hash,
            MediaType = MediaType.Image,
        };

        await using var db = PostgresServer.Context(connectionString);
        db.Users.Add(new User { Id = user, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.Add(new UserStorageConfig { Id = config, UserId = user, RootPath = "/data/originals/mira", IsActive = true });
        db.MediaAssets.AddRange(Photo(Moved, "2023/photo.jpg", "abc123"), Photo(AtPath, "2024/taken.jpg", "f00d"), Photo(Other, "2022/other.jpg", "beef"));
        await db.SaveChangesAsync(Ct);
        return connectionString;
    }

    private static async Task<Dictionary<Guid, (string Hash, bool SoftDeleted)>> RowsAsync(string connectionString)
    {
        await using var db = PostgresServer.Context(connectionString);
        return await db.MediaAssets.IgnoreQueryFilters().ToDictionaryAsync(a => a.Id, a => (a.ContentHash, a.IsSoftDeleted), Ct);
    }

    private static async Task<bool> TakeContentAsync(string connectionString, string hash)
    {
        await using var db = PostgresServer.Context(connectionString);
        var moved = await db.MediaAssets.SingleAsync(a => a.Id == Moved, Ct);
        var atPath = await db.MediaAssets.SingleAsync(a => a.Id == AtPath, Ct);
        moved.IsSoftDeleted = true;
        atPath.ContentHash = hash;
        return await new IngestionSaver(db, new EfMediaAssetRepository(db)).TrySaveRetiringFirstAsync(moved, Ct);
    }

    [Fact]
    public async Task TrySaveRetiringFirstAsync_ThePathsRowTakesTheRetiredRowsContentAsync()
    {
        var connectionString = await LibraryAsync();

        var saved = await TakeContentAsync(connectionString, "abc123");

        saved.Should().BeTrue();
        (await RowsAsync(connectionString)).Should().BeEquivalentTo(new Dictionary<Guid, (string, bool)>
        {
            [Moved] = ("abc123", true),
            [AtPath] = ("abc123", false),
            [Other] = ("beef", false),
        });
    }

    [Fact]
    public async Task TrySaveRetiringFirstAsync_ContentHeldByAnotherActiveRow_RollsBackTheSoftDeleteAsync()
    {
        var connectionString = await LibraryAsync();

        var saved = await TakeContentAsync(connectionString, "beef");

        saved.Should().BeFalse();
        (await RowsAsync(connectionString)).Should().BeEquivalentTo(new Dictionary<Guid, (string, bool)>
        {
            [Moved] = ("abc123", false),
            [AtPath] = ("f00d", false),
            [Other] = ("beef", false),
        });
    }
}
