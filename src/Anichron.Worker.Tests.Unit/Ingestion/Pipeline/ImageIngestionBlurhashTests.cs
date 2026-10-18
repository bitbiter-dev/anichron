using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NodaTime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class ImageIngestionBlurhashTests
{
    private static byte[] Jpeg()
    {
        using var image = new Image<Rgb24>(32, 16, Color.CornflowerBlue.ToPixel<Rgb24>());
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task NewImage_IsPersistedWithItsBlurhashAndNoBlurhashProxyAsync()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/nas/photo.jpg"] = new(Jpeg()),
        });
        var clock = Substitute.For<IClock>();
        var guids = Substitute.For<IGuidFactory>();
        guids.NewGuid().Returns(_ => Guid.NewGuid());
        var repository = Substitute.For<IMediaAssetRepository>();
        MediaAsset? persisted = null;
        repository.Add(Arg.Do<MediaAsset>(asset => persisted = asset));
        var proxies = new List<ProxyFile>();
        repository.AddProxyFile(Arg.Do<ProxyFile>(proxies.Add));
        var options = Options.Create(new WorkerSettings());
        var processor = new ImageSharpProcessor(options);
        var stagingWriter = new ProxyStagingWriter(fileSystem, clock, guids, new FakeLogger<ProxyStagingWriter>());
        IIngestionMiddleware[] middlewares =
        [
            new ImageProxyMiddleware(
                [new ThumbnailGenerator(processor, options), new FullPreviewGenerator(processor, options)],
                new BlurhashEncoder(processor, options),
                new TwoLevelHexShardStrategy(),
                stagingWriter,
                fileSystem,
                new FakeLogger<ImageProxyMiddleware>()),
            new PersistenceMiddleware(
                [
                    new NewContentWriter(
                        repository,
                        new IngestionSaver(Substitute.For<IUnitOfWork>(), repository),
                        fileSystem,
                        clock,
                        new PairedMovResolver(repository, fileSystem, clock, guids),
                        new PairedMovProxyWriter(
                            [], new TwoLevelHexShardStrategy(), stagingWriter, repository,
                            new FakeLogger<PairedMovProxyWriter>()),
                        new FakeLogger<NewContentWriter>()),
                ],
                new FakeLogger<PersistenceMiddleware>()),
        ];
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/nas/photo.jpg", "photo.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AssetId = Guid.NewGuid(),
            ContentHash = "0123456789abcdef",
            Classification = new(new FileClassification(FileOutcome.New, null), null, PairingAction.None),
            Exif = ExifData.Empty,
        };

        await IngestionPipelineBuilder.Build(middlewares, new FakeLogger())(context, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted.Metadata!.Blurhash.Should().HaveLength(28);
        proxies.Select(p => p.ProxyType).Should().BeEquivalentTo([ProxyType.Thumbnail, ProxyType.FullPreview]);
        fileSystem.AllFiles.Where(path => path.StartsWith(StoragePath.ProxiesRoot + "/", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo("thumbnail.jpg", "preview.jpg");
    }
}
