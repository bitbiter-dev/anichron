using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class BurstRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
