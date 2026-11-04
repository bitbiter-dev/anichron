using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data;

public sealed class MediaMappingTests
{
    private static CaptureDate InAuckland => new(
        new LocalDateTime(2026, 10, 30, 19, 9, 51), Offset.FromHours(13), CaptureSource.Recorded, "Pacific/Auckland");

    private static MediaDescription Camera => new(4032, 3024, 6, -36.8485, 174.7633, "Apple", "iPhone 15 Pro", "Main Camera", null);

    private static MediaAsset NewAsset(CaptureDate? captured, MediaDescription description)
    {
        var id = Guid.NewGuid();
        return new MediaAsset
        {
            Id = id,
            StorageConfigId = Guid.NewGuid(),
            FilePath = "IMG_0001.HEIC",
            FileName = "IMG_0001.HEIC",
            ContentHash = "cafe",
            Captured = captured,
            Metadata = new Metadata { AssetId = id, Description = description, Blurhash = "LEHV6n" },
        };
    }

    private static async Task<MediaAsset> SaveAndReloadAsync(MediaAsset asset)
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Guid.NewGuid().ToString();
        await using (var db = AnichronDbContexts.InMemory(name))
        {
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);
        }

        await using var reload = AnichronDbContexts.InMemory(name);
        return await reload.MediaAssets.Include(a => a.Metadata).SingleAsync(ct);
    }

    [Fact]
    public async Task Captured_SurvivesASaveWithItsOffsetSourceAndZone()
        => (await SaveAndReloadAsync(NewAsset(InAuckland, Camera))).Captured.Should().Be(InAuckland);

    [Fact]
    public async Task Description_SurvivesASaveBesideItsBlurhash()
    {
        var reloaded = await SaveAndReloadAsync(NewAsset(null, Camera));

        reloaded.Metadata.Should().BeEquivalentTo(new { Description = Camera, Blurhash = "LEHV6n" });
    }

    [Fact]
    public async Task Description_ReplacedOnAStoredRow_IsSavedInPlace()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = Guid.NewGuid().ToString();
        await using (var db = AnichronDbContexts.InMemory(name))
        {
            db.MediaAssets.Add(NewAsset(null, Camera));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = AnichronDbContexts.InMemory(name))
        {
            var metadata = await db.Set<Metadata>().SingleAsync(ct);
            metadata.Description = metadata.Description with { Width = 300 };
            await db.SaveChangesAsync(ct);
        }

        await using var reload = AnichronDbContexts.InMemory(name);
        (await reload.Set<Metadata>().SingleAsync(ct)).Description.Width.Should().Be(300);
    }

    [Fact]
    public void Description_IsRequiredOnEveryMetadataRow()
    {
        using var context = AnichronDbContexts.NpgsqlModel();

        var navigation = context.Model.FindEntityType(typeof(Metadata))!.FindNavigation(nameof(Metadata.Description))!;

        navigation.ForeignKey.IsRequiredDependent.Should().BeTrue();
    }

    [Fact]
    public void Description_IsStoredInTheMetadataTable()
    {
        using var context = AnichronDbContexts.NpgsqlModel();

        var description = context.Model.FindEntityType(typeof(MediaDescription))!;

        description.IsOwned().Should().BeTrue();
        description.GetTableName().Should().Be("Metadata");
    }

    [Fact]
    public void Unknown_IsEmptyAndUpright()
        => MediaDescription.Unknown.Should().Be(new MediaDescription(0, 0, 1, null, null, null, null, null, null));

    [Fact]
    public void Unknown_IsANewValueEachTime()
        => MediaDescription.Unknown.Should().NotBeSameAs(MediaDescription.Unknown);

    [Fact]
    public void Metadata_New_DescribesNothing()
        => new Metadata().Description.Should().Be(MediaDescription.Unknown);
}
