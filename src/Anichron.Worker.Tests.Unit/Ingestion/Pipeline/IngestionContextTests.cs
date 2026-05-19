using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class IngestionContextTests
{
    [Fact]
    public void ProxyFiles_DefaultsToEmptyList()
    {
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
        };
        context.ProxyFiles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Asset_CanBeSetAndRead()
    {
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
        };
        var asset = new MediaAsset();
        context.Asset = asset;
        context.Asset.Should().BeSameAs(asset);
    }
}
