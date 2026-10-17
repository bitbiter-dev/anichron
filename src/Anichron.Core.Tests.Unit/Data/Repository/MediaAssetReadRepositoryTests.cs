using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class MediaAssetReadRepositoryTests
{
    private sealed class Library
    {
        private readonly List<UserStorageConfig> configs = [];

        public Library()
        {
            var alice = new User { Id = Guid.NewGuid(), Username = "alice", Email = "alice@example.com" };
            var miraConfig = Config(Mira, "/data/originals/mira", isActive: true);
            var miraArchive = Config(Mira, "/data/originals/mira-archive", isActive: false);
            var aliceConfig = Config(alice, "/data/originals/alice", isActive: true);
            Config(Admin, "/data/originals/admin", isActive: true);
            MirasPhoto = Asset(miraConfig, "a.jpg", softDeleted: false);
            MirasTrashedPhoto = Asset(miraConfig, "b.jpg", softDeleted: true);
            MirasArchivedPhoto = Asset(miraArchive, "d.jpg", softDeleted: false);
            AlicesPhoto = Asset(aliceConfig, "c.jpg", softDeleted: false);
            Users = [Mira, Admin, alice];
        }

        public User Mira { get; } = new() { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com" };
        public User Admin { get; } = new() { Id = Guid.NewGuid(), Username = "admin", Email = "admin@example.com", IsAdmin = true };
        public MediaAsset MirasPhoto { get; }
        public MediaAsset MirasTrashedPhoto { get; }
        public MediaAsset MirasArchivedPhoto { get; }
        public MediaAsset AlicesPhoto { get; }
        public User[] Users { get; }
        public IReadOnlyList<UserStorageConfig> Configs => configs;
        public MediaAsset[] Assets => [MirasPhoto, MirasTrashedPhoto, MirasArchivedPhoto, AlicesPhoto];

        public ProxyFile[] Proxies
        {
            get
            {
                return
                [
                    Proxy(MirasPhoto, ProxyType.Thumbnail),
                    Proxy(MirasPhoto, ProxyType.FullPreview),
                    Proxy(MirasTrashedPhoto, ProxyType.Thumbnail),
                    Proxy(AlicesPhoto, ProxyType.Thumbnail),
                ];
            }
        }

        public static string PathOf(MediaAsset asset, ProxyType type) => $"{asset.ContentHash}/{type}";

        private static ProxyFile Proxy(MediaAsset asset, ProxyType type)
            => new() { Id = Guid.NewGuid(), AssetId = asset.Id, ProxyType = type, ProxyPath = PathOf(asset, type) };

        private UserStorageConfig Config(User owner, string rootPath, bool isActive)
        {
            var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = owner.Id, RootPath = rootPath, IsActive = isActive };
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

    private static async Task<(DbContextOptions<AnichronDbContext> Options, Library Library)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AnichronDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var library = new Library();
        await using var db = new AnichronDbContext(options);
        db.Users.AddRange(library.Users);
        db.StorageConfigs.AddRange(library.Configs);
        db.MediaAssets.AddRange(library.Assets);
        db.ProxyFiles.AddRange(library.Proxies);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (options, library);
    }

    [Fact]
    public async Task VisibleTo_WithQueryFiltersIgnored_StillExcludesSoftDeletedAndInactiveAssets()
    {
        var (options, library) = await SeedAsync();
        await using var db = new AnichronDbContext(options);

        var visible = await db.MediaAssets.IgnoreQueryFilters().VisibleTo(library.Mira.Id)
            .Select(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        visible.Should().Equal(library.MirasPhoto.Id);
    }

    // ==========================================================================
    // FindVisibleProxyAsync
    // ==========================================================================

    private static async Task<(VisibleProxy? Found, Library Library)> FindProxyAsync(
        Func<Library, Guid> assetId, Func<Library, Guid> caller, ProxyType type)
    {
        var (options, library) = await SeedAsync();
        await using var db = new AnichronDbContext(options);
        var found = await new EfMediaAssetReadRepository(db)
            .FindVisibleProxyAsync(assetId(library), caller(library), type, TestContext.Current.CancellationToken);
        return (found, library);
    }

    [Theory]
    [InlineData(ProxyType.Thumbnail)]
    [InlineData(ProxyType.FullPreview)]
    public async Task FindVisibleProxyAsync_OwnAssetWithThatProxy_ReturnsItsPathAndContentHash(ProxyType type)
    {
        var (found, library) = await FindProxyAsync(l => l.MirasPhoto.Id, l => l.Mira.Id, type);

        found.Should().Be(new VisibleProxy(Library.PathOf(library.MirasPhoto, type), library.MirasPhoto.ContentHash));
    }

    [Fact]
    public async Task FindVisibleProxyAsync_OwnAssetWithoutThatProxy_ReturnsNull()
        => (await FindProxyAsync(l => l.MirasPhoto.Id, l => l.Mira.Id, ProxyType.WebVideo)).Found.Should().BeNull();

    [Fact]
    public async Task FindVisibleProxyAsync_AnotherUsersAsset_ReturnsNull()
        => (await FindProxyAsync(l => l.AlicesPhoto.Id, l => l.Mira.Id, ProxyType.Thumbnail)).Found.Should().BeNull();

    [Fact]
    public async Task FindVisibleProxyAsync_SoftDeletedOwnAsset_ReturnsNull()
        => (await FindProxyAsync(l => l.MirasTrashedPhoto.Id, l => l.Mira.Id, ProxyType.Thumbnail)).Found.Should().BeNull();

    // ==========================================================================
    // Generated SQL
    // ==========================================================================

    private static AnichronDbContext CreateNpgsqlModelOnlyDb()
        => new(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql("Host=unused", o => o.UseNodaTime())
            .EnableServiceProviderCaching(false)
            .Options);

    [Fact]
    public void VisibleTo_TranslatesToAJoinOnTheActiveOwnedConfig()
    {
        using var db = CreateNpgsqlModelOnlyDb();
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var sql = db.MediaAssets.VisibleTo(userId).ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"UserId\" = @")
            .And.Contain("11111111-2222-3333-4444-555555555555")
            .And.Contain("\"IsActive\"");
    }

    [Fact]
    public void VisibleTo_ComposedWithAnotherFilter_KeepsBothConditions()
    {
        using var db = CreateNpgsqlModelOnlyDb();

        var sql = db.MediaAssets.VisibleTo(Guid.NewGuid()).Where(a => a.Month == 10).ToQueryString();

        sql.Should().Contain("\"UserId\" = @").And.Contain("\"Month\" = 10");
    }

    [Fact]
    public void FindVisibleProxy_TranslatesThroughTheVisibilityRule()
    {
        using var db = CreateNpgsqlModelOnlyDb();

        var sql = EfMediaAssetReadRepository.VisibleProxyQuery(db, Guid.NewGuid(), Guid.NewGuid(), ProxyType.Thumbnail)
            .ToQueryString();

        sql.Should().Contain("JOIN \"StorageConfigs\"")
            .And.Contain("\"UserId\" = @")
            .And.Contain("\"ProxyType\" = @")
            .And.Contain("@proxyType='Thumbnail'");
    }
}
