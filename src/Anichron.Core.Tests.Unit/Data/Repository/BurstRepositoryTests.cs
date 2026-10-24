using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

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

    [Fact]
    public async Task GetUncheckedPhotosAsync_AfterAPhoto_ReturnsOnlyThoseThatComeAfterItAsync()
    {
        var name = Guid.NewGuid().ToString();
        var configId = Guid.NewGuid();
        var noon = new LocalDateTime(2024, 6, 1, 12, 0);
        var lowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var highId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var earlier = Photo(configId, Guid.NewGuid(), noon.PlusMinutes(-1));
        var atCursor = Photo(configId, lowId, noon);
        var sameTimeLaterId = Photo(configId, highId, noon);
        var later = Photo(configId, Guid.NewGuid(), noon.PlusMinutes(1));
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.MediaAssets.AddRange(later, sameTimeLaterId, atCursor, earlier);
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = AnichronDbContexts.InMemory(name);
        var photos = await new EfBurstRepository(db).GetUncheckedPhotosAsync(configId, atCursor, 10, Ct);

        photos.Select(p => p.Id).Should().Equal(highId, later.Id);
    }

    [Fact]
    public async Task GetUncheckedPhotosAsync_NoCursor_StartsAtTheOldestAsync()
    {
        var name = Guid.NewGuid().ToString();
        var configId = Guid.NewGuid();
        var noon = new LocalDateTime(2024, 6, 1, 12, 0);
        var older = Photo(configId, Guid.NewGuid(), noon);
        var newer = Photo(configId, Guid.NewGuid(), noon.PlusMinutes(1));
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.MediaAssets.AddRange(newer, older);
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = AnichronDbContexts.InMemory(name);
        var photos = await new EfBurstRepository(db).GetUncheckedPhotosAsync(configId, null, 10, Ct);

        photos.Select(p => p.Id).Should().Equal(older.Id, newer.Id);
    }

    [Fact]
    public void GetUncheckedPhotosAsync_OnPostgres_ComparesCaptureTimeThenIdInTheQuery()
    {
        using var db = AnichronDbContexts.NpgsqlModel();
        var after = Photo(Guid.NewGuid(), Guid.NewGuid(), new LocalDateTime(2024, 6, 1, 12, 0));

        var sql = new EfBurstRepository(db).UncheckedPhotosAfter(after.StorageConfigId, after).ToQueryString();

        sql.Should().Contain("""m."DateCaptured" > @capturedAt""")
           .And.Contain("""m."DateCaptured" = @capturedAt AND m."Id" > @id""");
    }

    private static MediaAsset Photo(Guid configId, Guid id, LocalDateTime capturedAt) => new()
    {
        Id = id,
        StorageConfigId = configId,
        FilePath = $"{id}.heic",
        FileName = $"{id}.heic",
        ContentHash = id.ToString("N"),
        MediaType = MediaType.Image,
        DateCaptured = capturedAt,
    };
}
