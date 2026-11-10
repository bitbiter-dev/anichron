using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Extraction;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Anichron.Worker.Tests.Unit.Ingestion.Classification;
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

    private sealed record Ingested(MediaAsset? Asset, IReadOnlyList<ProxyFile> ProxyRows, IEnumerable<string> ProxyFileNames);

    private static async Task<Ingested> IngestNewAsync(SingleFileItem item, MockFileSystem fileSystem, IVideoFrameExtractor frameExtractor)
    {
        var clock = Substitute.For<IClock>();
        var guids = Substitute.For<IGuidFactory>();
        guids.NewGuid().Returns(_ => Guid.NewGuid());
        var repository = Substitute.For<IMediaAssetRepository>();
        MediaAsset? persisted = null;
        repository.Add(Arg.Do<MediaAsset>(asset => persisted = asset));
        var proxyRows = new List<ProxyFile>();
        repository.AddProxyFiles(Arg.Do<IEnumerable<ProxyFile>>(proxyRows.AddRange));
        var options = Options.Create(new WorkerSettings());
        var processor = new ImageSharpProcessor(options);
        var stagingWriter = new ProxyStagingWriter(fileSystem, clock, guids, new FakeLogger<ProxyStagingWriter>());
        var videoProcessor = Substitute.For<IVideoProcessor>();
        videoProcessor.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => fileSystem.File.WriteAllBytesAsync(call.ArgAt<string>(1), [0x01], CancellationToken.None));
        IIngestionMiddleware[] middlewares =
        [
            new ImageProxyMiddleware(
                [new ThumbnailGenerator(processor, options), new FullPreviewGenerator(processor, options)],
                new BlurhashEncoder(processor, options),
                new UprightImageDecoder(new MagickHeifDecoder(), new ImageSharpUprightDecoder()),
                frameExtractor,
                new TwoLevelHexShardStrategy(),
                stagingWriter,
                fileSystem,
                new FakeLogger<ImageProxyMiddleware>()),
            new VideoProxyMiddleware(
                [new WebVideoGenerator(videoProcessor)],
                new TwoLevelHexShardStrategy(),
                stagingWriter,
                new FakeLogger<VideoProxyMiddleware>()),
            new PersistenceMiddleware(
                [
                    new NewContentWriter(
                        repository,
                        clock,
                        new PairedMovResolver(
                            repository, new StoredContent(repository, Substitute.For<IBurstRepository>(), clock, new FakeLogger<StoredContent>()),
                            clock, guids, new FakeLogger<PairedMovResolver>()),
                        new PairedMovProxyWriter(
                            [], new TwoLevelHexShardStrategy(), stagingWriter, repository,
                            new FakeLogger<PairedMovProxyWriter>()),
                        new CaptureZones(DateTimeZoneProviders.Tzdb, DateTimeZone.Utc, new FakeLogger<CaptureZones>())),
                ],
                new IngestionSaver(Substitute.For<IUnitOfWork>(), repository),
                new FakeLogger<PersistenceMiddleware>()),
        ];
        var context = new IngestionContext
        {
            Item = item,
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/nas" },
            AttemptId = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            ContentHash = "0123456789abcdef",
            Classification = Classifications.Of(FileOutcome.New),
            Extracted = ExtractedMedia.Empty,
        };

        await IngestionPipelineBuilder.Build(middlewares, new FakeLogger())(context, CancellationToken.None);

        return new(
            persisted,
            proxyRows,
            fileSystem.AllFiles.Where(path => path.StartsWith(StoragePath.ProxiesRoot + "/", StringComparison.Ordinal))
                .Select(path => Path.GetFileName(path)));
    }

    [Fact]
    public async Task NewImage_IsPersistedWithItsBlurhashAndNoBlurhashProxyAsync()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/nas/photo.jpg"] = new(Jpeg()),
        });

        var ingested = await IngestNewAsync(
            new SingleFileItem("/nas/photo.jpg", "photo.jpg", MediaType.Image), fileSystem, Substitute.For<IVideoFrameExtractor>());

        ingested.Asset.Should().NotBeNull();
        ingested.Asset.Metadata!.Blurhash.Should().HaveLength(28);
        ingested.ProxyRows.Should().OnlyContain(p => p.AssetId == ingested.Asset.Id);
        ingested.ProxyRows.Select(p => p.ProxyType).Should().BeEquivalentTo([ProxyType.Thumbnail, ProxyType.FullPreview]);
        ingested.ProxyFileNames.Should().BeEquivalentTo("thumbnail.jpg", "preview.jpg");
    }

    [Fact]
    public async Task NewVideo_IsPersistedWithItsPosterBlurhashAndWebVideoAsync()
    {
        var frameExtractor = Substitute.For<IVideoFrameExtractor>();
        frameExtractor.ExtractAsync("/nas/clip.mov", Arg.Any<CancellationToken>())
            .Returns(_ => new Image<Rgb24>(64, 36, Color.CornflowerBlue.ToPixel<Rgb24>()));

        var ingested = await IngestNewAsync(
            new SingleFileItem("/nas/clip.mov", "clip.mov", MediaType.Video), new MockFileSystem(), frameExtractor);

        ingested.Asset.Should().NotBeNull();
        ingested.Asset.MediaType.Should().Be(MediaType.Video);
        ingested.Asset.Metadata!.Blurhash.Should().HaveLength(28);
        ingested.ProxyRows.Should().OnlyContain(p => p.AssetId == ingested.Asset.Id);
        ingested.ProxyRows.Select(p => p.ProxyType)
            .Should().BeEquivalentTo([ProxyType.Thumbnail, ProxyType.FullPreview, ProxyType.WebVideo]);
        ingested.ProxyFileNames.Should().BeEquivalentTo("thumbnail.jpg", "preview.jpg", "video.mp4");
    }
}
