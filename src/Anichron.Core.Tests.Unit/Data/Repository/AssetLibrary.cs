using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Testing.Domain;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

internal sealed class AssetLibrary
{
    private readonly List<UserStorageConfig> configs = [];
    private readonly List<MediaAsset> assets = [];
    private readonly List<ProxyFile> proxies = [];
    private readonly List<AssetInteraction> interactions = [];
    private readonly List<Burst> bursts = [];

    public AssetLibrary()
    {
        MiraConfig = Config(Mira, "/data/originals/mira", isActive: true);
        AliceConfig = Config(Alice, "/data/originals/alice", isActive: true);
        MiraArchive = Config(Mira, "/data/originals/mira-archive", isActive: false);
    }

    public User Mira { get; } = new() { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com" };
    public User Alice { get; } = new() { Id = Guid.NewGuid(), Username = "alice", Email = "alice@example.com" };
    public UserStorageConfig MiraConfig { get; }
    public UserStorageConfig AliceConfig { get; }
    public UserStorageConfig MiraArchive { get; }

    public MediaAsset Asset(
        UserStorageConfig config,
        LocalDateTime? captured = null,
        MediaType type = MediaType.Image,
        bool softDeleted = false,
        Metadata? metadata = null,
        Guid? id = null,
        params ProxyType[] proxyTypes)
    {
        var when = captured ?? new LocalDateTime(2019, 7, 14, 9, 30);
        var asset = new MediaAsset
        {
            Id = id ?? Guid.NewGuid(),
            StorageConfigId = config.Id,
            FilePath = $"{Guid.NewGuid():N}.jpg",
            FileName = "x.jpg",
            ContentHash = Guid.NewGuid().ToString("N"),
            Captured = Recorded.At(when),
            MediaType = type,
            IsSoftDeleted = softDeleted,
            Metadata = metadata,
        };
        assets.Add(asset);
        proxies.AddRange(proxyTypes.Select(t => new ProxyFile
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            ProxyType = t,
            ProxyPath = $"{asset.ContentHash}/{t}",
        }));
        return asset;
    }

    public MediaAsset LivePhoto(MediaAsset mov, LocalDateTime? captured = null, bool softDeleted = false)
    {
        var heic = Asset(MiraConfig, captured, MediaType.LivePhoto, softDeleted);
        heic.PairedAssetId = mov.Id;
        return heic;
    }

    public void Interaction(
        User user, MediaAsset asset, bool starred = false, bool liked = false, bool hidden = false, Instant? hiddenAt = null)
        => interactions.Add(new AssetInteraction
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AssetId = asset.Id,
            IsStarred = starred,
            IsLiked = liked,
            HiddenAt = hiddenAt ?? (hidden ? Instant.FromUtc(2026, 11, 1, 12, 0) : null),
        });

    public Burst Burst(params MediaAsset[] members)
    {
        var burst = new Burst { Id = Guid.NewGuid(), PrimaryAssetId = members[0].Id };
        bursts.Add(burst);
        foreach (var member in members)
            member.BurstId = burst.Id;
        return burst;
    }

    public async Task<DbContextOptions<AnichronDbContext>> SeedAsync()
    {
        var options = AnichronDbContexts.InMemoryOptions();
        await using var db = new AnichronDbContext(options);
        db.Users.AddRange(Mira, Alice);
        db.StorageConfigs.AddRange(configs);
        db.MediaAssets.AddRange(assets);
        db.ProxyFiles.AddRange(proxies);
        db.Interactions.AddRange(interactions);
        db.Bursts.AddRange(bursts);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return options;
    }

    private UserStorageConfig Config(User owner, string rootPath, bool isActive)
    {
        var config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = owner.Id, RootPath = rootPath, IsActive = isActive };
        configs.Add(config);
        return config;
    }
}
