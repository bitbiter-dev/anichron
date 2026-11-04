using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Anichron.Tests.Integration.Data;

public sealed class MetadataDescriptionTests(PostgresServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MediaDescription Camera => new(4032, 3024, 6, -36.8485, 174.7633, "Apple", "iPhone 15 Pro", "Main Camera", null);

    private static CaptureDate InAuckland => new(
        new LocalDateTime(2026, 10, 30, 19, 9, 51), Offset.FromHours(13), CaptureSource.Recorded, "Pacific/Auckland");

    private async Task<(string ConnectionString, Guid AssetId)> StoredPhotoAsync()
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        var (user, config, asset) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = PostgresServer.Context(connectionString);
        db.Users.Add(new User { Id = user, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.Add(new UserStorageConfig { Id = config, UserId = user, RootPath = "/data/originals/mira", IsActive = true });
        db.MediaAssets.Add(new MediaAsset
        {
            Id = asset,
            StorageConfigId = config,
            FilePath = "IMG_0001.HEIC",
            FileName = "IMG_0001.HEIC",
            ContentHash = "cafe",
            MediaType = MediaType.Image,
            Captured = InAuckland,
            Metadata = new Metadata { AssetId = asset, Description = Camera, Blurhash = "LEHV6n" },
        });
        await db.SaveChangesAsync(Ct);
        return (connectionString, asset);
    }

    [Fact]
    public async Task ReplacedDescription_UpdatesTheMetadataRowInPlaceAsync()
    {
        var (connectionString, assetId) = await StoredPhotoAsync();
        var commands = new List<string>();
        await using (var db = new AnichronDbContext(new DbContextOptionsBuilder<AnichronDbContext>()
            .UseNpgsql(connectionString, o => o.UseNodaTime())
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted], LogLevel.Information)
            .Options))
        {
            var metadata = await db.Metadata.SingleAsync(m => m.AssetId == assetId, Ct);
            commands.Clear();
            metadata.Description = metadata.Description with { Width = 300 };
            await db.SaveChangesAsync(Ct);
        }

        commands.Should().ContainSingle().Which.Should().Contain("UPDATE \"Metadata\"").And.NotContain("DELETE");
        await using var reload = PostgresServer.Context(connectionString);
        (await reload.Metadata.SingleAsync(m => m.AssetId == assetId, Ct))
            .Should().BeEquivalentTo(new { Description = Camera with { Width = 300 }, Blurhash = "LEHV6n" });
    }
}
