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

    [Fact]
    public void ReleaseProxyFiles_DropsOnlyThatOwnersRegistrations()
    {
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
        };
        var movId = Guid.NewGuid();
        var own = new ProxyFile { AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail };
        context.AddProxyFile(own);
        context.AddProxyFile(new ProxyFile { AssetId = movId, ProxyType = ProxyType.WebVideo });

        context.ReleaseProxyFiles(movId);

        context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(own);
    }

    [Fact]
    public void ProxyFilesOf_ReturnsOnlyThatOwnersRegistrations()
    {
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
        };
        var movId = Guid.NewGuid();
        var mov = new ProxyFile { AssetId = movId, ProxyType = ProxyType.WebVideo };
        context.AddProxyFile(new ProxyFile { AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail });
        context.AddProxyFile(mov);

        context.ProxyFilesOf(movId).Should().ContainSingle().Which.Should().BeSameAs(mov);
    }
}
