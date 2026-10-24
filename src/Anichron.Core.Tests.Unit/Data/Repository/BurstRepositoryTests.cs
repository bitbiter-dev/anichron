using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class BurstRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LocalDateTime Noon = new(2024, 7, 15, 12, 0, 0);

    private static object[] Photo(
        string name, Guid config, int seconds, string? make = "Apple", string? model = "iPhone 15 Pro",
        bool? candidate = true, bool softDeleted = false, Guid? id = null, ProxyType[]? proxies = null)
    {
        var assetId = id ?? Guid.NewGuid();
        var asset = new MediaAsset
        {
            Id = assetId,
            StorageConfigId = config,
            FilePath = name,
            FileName = name,
            ContentHash = name,
            DateCaptured = Noon.PlusSeconds(seconds),
            IsSoftDeleted = softDeleted,
            Metadata = new Metadata { AssetId = assetId, CameraMake = make, CameraModel = model },
            ProxyFiles = [.. (proxies ?? [ProxyType.Thumbnail]).Select(type => new ProxyFile { Id = Guid.NewGuid(), AssetId = assetId, ProxyType = type, ProxyPath = $"{name}/{type}" })],
        };
        return candidate is { } isCandidate ? [asset, new BurstCheck { AssetId = assetId, IsCandidate = isCandidate }] : [asset];
    }

    [Fact]
    public async Task GetCandidatesNearAsync_ReturnsTheConfigsCheckedCandidatesOfThatCameraInsideTheWindowOldestFirstAsync()
    {
        var name = Guid.NewGuid().ToString();
        var (config, other) = (Guid.NewGuid(), Guid.NewGuid());
        var (low, high) = (Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.AddRange((IEnumerable<object>)[
                .. Photo("at-start.jpg", config, -120),
                .. Photo("tie-high.jpg", config, 0, id: high),
                .. Photo("tie-low.jpg", config, 0, id: low),
                .. Photo("at-end.jpg", config, 120, proxies: [ProxyType.Thumbnail, ProxyType.FullPreview]),
                .. Photo("too-early.jpg", config, -121),
                .. Photo("too-late.jpg", config, 121),
                .. Photo("other-make.jpg", config, 0, make: "Canon"),
                .. Photo("other-model.jpg", config, 0, model: "iPhone 14"),
                .. Photo("other-config.jpg", other, 0),
                .. Photo("unchecked.jpg", config, 0, candidate: null),
                .. Photo("no-candidate.jpg", config, 0, candidate: false),
                .. Photo("deleted.jpg", config, 0, softDeleted: true),
                .. Photo("no-thumbnail.jpg", config, 0, proxies: [ProxyType.FullPreview])]);
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = AnichronDbContexts.InMemory(name);
        var near = await new EfBurstRepository(db).GetCandidatesNearAsync(
            config, "Apple", "iPhone 15 Pro", Noon.PlusSeconds(-120), Noon.PlusSeconds(120), Ct);

        near.Select(a => a.FileName).Should().Equal("at-start.jpg", "tie-low.jpg", "tie-high.jpg", "at-end.jpg");
        near.Should().AllSatisfy(a => a.ProxyFiles.Should().Contain(p => p.ProxyType == ProxyType.Thumbnail));
    }

    [Fact]
    public async Task GetInConfigWithMembersAsync_LeavesOutBurstsWithoutMembersAsync()
    {
        var name = Guid.NewGuid().ToString();
        var config = Guid.NewGuid();
        var (withMember, memberless) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.Bursts.AddRange(new Burst { Id = withMember }, new Burst { Id = memberless });
            seed.MediaAssets.Add(new MediaAsset { Id = Guid.NewGuid(), StorageConfigId = config, BurstId = withMember, FilePath = "a.jpg", FileName = "a.jpg", ContentHash = "cafe" });
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = AnichronDbContexts.InMemory(name);
        var loaded = await new EfBurstRepository(db).GetInConfigWithMembersAsync(config, Ct);

        loaded.Should().ContainSingle().Which.Id.Should().Be(withMember);
    }

    [Fact]
    public async Task RemoveCheckAsync_CheckedPhoto_DeletesItsRowAtTheNextSaveAsync()
    {
        var name = Guid.NewGuid().ToString();
        var asset = new MediaAsset { Id = Guid.NewGuid(), StorageConfigId = Guid.NewGuid(), FilePath = "a.jpg", FileName = "a.jpg", ContentHash = "cafe" };
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.MediaAssets.Add(asset);
            seed.BurstChecks.Add(new BurstCheck { AssetId = asset.Id, IsCandidate = true });
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = AnichronDbContexts.InMemory(name);
        await new EfBurstRepository(db).RemoveCheckAsync(asset.Id, Ct);
        await db.SaveChangesAsync(Ct);

        (await db.BurstChecks.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task RemoveCheckAsync_UncheckedPhoto_ChangesNothingAsync()
    {
        await using var db = AnichronDbContexts.InMemory();

        await new EfBurstRepository(db).RemoveCheckAsync(Guid.NewGuid(), Ct);

        db.ChangeTracker.HasChanges().Should().BeFalse();
    }
}
