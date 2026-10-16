using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class MediaAssetReadRepositoryTests
{
    private sealed class Library
    {
        public User Mira { get; } = new() { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com" };
        public User Admin { get; } = new() { Id = Guid.NewGuid(), Username = "admin", Email = "admin@example.com", IsAdmin = true };
        public MediaAsset MirasPhoto { get; }
        public MediaAsset MirasTrashedPhoto { get; }
        public MediaAsset AlicesPhoto { get; }

        private readonly List<UserStorageConfig> configs = [];

        public Library()
        {
            var alice = new User { Id = Guid.NewGuid(), Username = "alice", Email = "alice@example.com" };
            var miraConfig = Config(Mira, "/data/originals/mira");
            var aliceConfig = Config(alice, "/data/originals/alice");
            Config(Admin, "/data/originals/admin");
            MirasPhoto = Asset(miraConfig, "a.jpg", softDeleted: false);
            MirasTrashedPhoto = Asset(miraConfig, "b.jpg", softDeleted: true);
            AlicesPhoto = Asset(aliceConfig, "c.jpg", softDeleted: false);
            Users = [Mira, Admin, alice];
        }

        public User[] Users { get; }
        public IReadOnlyList<UserStorageConfig> Configs => configs;
        public MediaAsset[] Assets => [MirasPhoto, MirasTrashedPhoto, AlicesPhoto];

        private UserStorageConfig Config(User owner, string rootPath)
        {
            var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = owner.Id, RootPath = rootPath, IsActive = true };
            configs.Add(config);
            return config;
        }

        private static MediaAsset Asset(UserStorageConfig config, string name, bool softDeleted)
            => new()
            {
                Id = Guid.NewGuid(),
                StorageConfigId = config.Id,
                FilePath = name,
                FileName = name,
                ContentHash = name,
                IsSoftDeleted = softDeleted,
            };
    }

    private static async Task<(MediaAsset? Found, Library Library)> FindAsync(
        Func<Library, Guid> assetId, Func<Library, Guid> caller)
    {
        var ct = TestContext.Current.CancellationToken;
        var library = new Library();
        await using var db = new AnichronDbContext(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Users.AddRange(library.Users);
        db.StorageConfigs.AddRange(library.Configs);
        db.MediaAssets.AddRange(library.Assets);
        await db.SaveChangesAsync(ct);

        var found = await new EfMediaAssetReadRepository(db).FindVisibleAsync(assetId(library), caller(library), ct);
        return (found, library);
    }

    [Fact]
    public async Task FindVisibleAsync_OwnAsset_ReturnsIt()
    {
        var (found, library) = await FindAsync(l => l.MirasPhoto.Id, l => l.Mira.Id);

        found.Should().NotBeNull();
        found.Id.Should().Be(library.MirasPhoto.Id);
    }

    [Fact]
    public async Task FindVisibleAsync_AnotherUsersAsset_ReturnsNull()
        => (await FindAsync(l => l.AlicesPhoto.Id, l => l.Mira.Id)).Found.Should().BeNull();

    [Fact]
    public async Task FindVisibleAsync_SoftDeletedOwnAsset_ReturnsNull()
        => (await FindAsync(l => l.MirasTrashedPhoto.Id, l => l.Mira.Id)).Found.Should().BeNull();

    [Fact]
    public async Task FindVisibleAsync_AdminAskingForAnotherUsersAsset_ReturnsNull()
        => (await FindAsync(l => l.AlicesPhoto.Id, l => l.Admin.Id)).Found.Should().BeNull();

    [Fact]
    public async Task FindVisibleAsync_UnknownId_ReturnsNull()
        => (await FindAsync(_ => Guid.NewGuid(), l => l.Mira.Id)).Found.Should().BeNull();

    // ==========================================================================
    // Generated SQL (InMemory does not prove the Npgsql translation)
    // ==========================================================================

    private static AnichronDbContext CreateNpgsqlModelOnlyDb()
        => new(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql("Host=unused", o => o.UseNodaTime())
            .EnableServiceProviderCaching(false)
            .Options);

    [Fact]
    public void VisibleTo_TranslatesToAJoinOnTheOwnerAndKeepsTheSoftDeleteFilter()
    {
        using var db = CreateNpgsqlModelOnlyDb();
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var sql = db.MediaAssets.VisibleTo(userId).ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"UserId\" = @")
            .And.Contain("11111111-2222-3333-4444-555555555555")
            .And.Contain("\"IsSoftDeleted\"");
    }

    [Fact]
    public void VisibleTo_ComposedWithAnotherFilter_KeepsBothConditions()
    {
        using var db = CreateNpgsqlModelOnlyDb();

        var sql = db.MediaAssets.VisibleTo(Guid.NewGuid()).Where(a => a.Month == 10).ToQueryString();

        sql.Should().Contain("\"UserId\" = @").And.Contain("\"Month\" = 10");
    }
}
