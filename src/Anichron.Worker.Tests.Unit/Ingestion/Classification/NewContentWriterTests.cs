using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using NSubstitute.ExceptionExtensions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class NewContentWriterTests
{
    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public MockFileSystem FileSystem { get; }
        public Instant Now { get; }

        public TestFixture()
        {
            Now = Instant.FromUtc(2026, 5, 19, 10, 0, 0);
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            Clock = clock;
            FileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                ["/abs/photo.jpg"] = new MockFileData([])
                {
                    LastWriteTime = new DateTimeOffset(new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Local)),
                },
            });
        }

        public IClock Clock { get; }

        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();

        public IVideoProxyGenerator MovGenerator { get; } = Substitute.For<IVideoProxyGenerator>();

        public NewContentWriter Build()
        {
            MovGenerator.FileName.Returns("video_720p.mp4");
            MovGenerator.ProxyType.Returns(ProxyType.WebVideo);
            MovGenerator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => FileSystem.File.WriteAllBytesAsync(call.ArgAt<string>(1), [0x01]));
            var stagingWriter = new ProxyStagingWriter(
                FileSystem,
                Clock,
                GuidFactory,
                new FakeLogger<ProxyStagingWriter>());
            return new(Repository, new IngestionSaver(UnitOfWork, Repository), FileSystem, Clock,
                new PairedMovResolver(Repository, FileSystem, Clock, GuidFactory),
                new PairedMovProxyWriter(
                    [MovGenerator], new TwoLevelHexShardStrategy(), stagingWriter, Repository,
                    new FakeLogger<PairedMovProxyWriter>()),
                new FakeLogger<NewContentWriter>());
        }
    }

    private static readonly ExifData DefaultExif =
        new(100, 200, 0, new LocalDateTime(2023, 6, 15, 10, 0), null, null, null, null, null, null);

    private static IngestionContext MakeContext(
        IngestionItem? item = null,
        string? contentHash = "deadbeef",
        ExifData? exif = null,
        MediaAsset? storedMov = null)
    {
        var resolvedItem = item ?? new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image);
        var isPair = resolvedItem.SecondaryFile is not null;
        var movOutcome = storedMov is null ? FileOutcome.New : FileOutcome.Unchanged;
        return new IngestionContext
        {
            Item = resolvedItem,
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
            ContentHash = contentHash,
            SecondaryHash = isPair ? "cafebabe" : null,
            Classification = new IngestionClassification(
                new FileClassification(FileOutcome.New, null),
                isPair ? new FileClassification(movOutcome, storedMov) : null,
                PairingAction.None),
            Exif = exif ?? DefaultExif,
        };
    }

    [Fact]
    public void Outcome_IsNew()
        => new TestFixture().Build().Outcome.Should().Be(FileOutcome.New);

    // ==========================================================================
    // TryWriteAsync
    // ==========================================================================

    [Fact]
    public async Task TryWriteAsync_Always_AddsAssetToRepositoryAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        await fx.Build().TryWriteAsync(context, CancellationToken.None);

        fx.Repository.Received(1).Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task TryWriteAsync_Always_CallsSaveChangesAsync()
    {
        var fx = new TestFixture();

        await fx.Build().TryWriteAsync(MakeContext(), CancellationToken.None);

        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryWriteAsync_Committed_ReturnsTheAddedAssetAsync()
    {
        var fx = new TestFixture();
        MediaAsset? added = null;
        fx.Repository.When(r => r.Add(Arg.Any<MediaAsset>())).Do(call => added = call.Arg<MediaAsset>());

        var asset = await fx.Build().TryWriteAsync(MakeContext(), CancellationToken.None);

        asset.Should().NotBeNull().And.BeSameAs(added);
    }

    [Fact]
    public async Task TryWriteAsync_WithExifDateCaptured_SetsMonthDayYearFromExifAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.Month.Should().Be(6);
        asset.Day.Should().Be(15);
        asset.Year.Should().Be(2023);
    }

    [Fact]
    public async Task TryWriteAsync_ExifDateDiffersFromTheFileDate_TheExifDateWinsAsync()
    {
        var fx = new TestFixture();
        var exifDate = new LocalDateTime(2020, 1, 2, 3, 4, 5);
        var context = MakeContext(exif: DefaultExif with { DateCaptured = exifDate });

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.DateCaptured.Should().Be(exifDate);
        (asset.Year, asset.Month, asset.Day).Should().Be((2020, 1, 2));
    }

    [Fact]
    public async Task TryWriteAsync_NewAsset_IsNotSoftDeletedAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.IsSoftDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task TryWriteAsync_WithNullDateCaptured_FallsBackToFileLastWriteTimeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(exif: DefaultExif with { DateCaptured = null });

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.Year.Should().Be(2023);
        asset.Month.Should().Be(6);
        asset.Day.Should().Be(15);
    }

    [Fact]
    public async Task TryWriteAsync_Always_SetsLastSeenOnNasFromClockAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.LastSeenOnNas.Should().Be(fx.Now);
    }

    [Fact]
    public async Task TryWriteAsync_Always_SetsMetadataFromExifAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.Metadata!.Width.Should().Be(100);
        asset.Metadata.Height.Should().Be(200);
    }

    [Fact]
    public async Task TryWriteAsync_Always_SetsMetadataBlurhashFromContextAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        context.Blurhash = "LEHV6nWB2yk8pyo0adR*.7kCMdnj";

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.Metadata!.Blurhash.Should().Be("LEHV6nWB2yk8pyo0adR*.7kCMdnj");
    }

    [Fact]
    public async Task TryWriteAsync_SingleFileItem_UsesItemMediaTypeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(item: new SingleFileItem("/abs/video.mp4", "video.mp4", MediaType.Video));

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.MediaType.Should().Be(MediaType.Video);
    }

    [Fact]
    public async Task TryWriteAsync_Always_SetsContentHashFromContextAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(contentHash: "deadbeef");

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.ContentHash.Should().Be("deadbeef");
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairItem_HeicHasLivePhotoMediaTypeAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.MediaType.Should().Be(MediaType.LivePhoto);
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairItem_AddsTwoAssetsToRepositoryAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().TryWriteAsync(context, CancellationToken.None);

        fx.Repository.Received(2).Add(Arg.Any<MediaAsset>());
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairItem_MovHasVideoMediaTypeAsync()
    {
        MediaAsset? movAsset = null;
        var fx = new TestFixture();
        fx.Repository.When(r => r.Add(Arg.Is<MediaAsset>(a => a.MediaType == MediaType.Video)))
            .Do(call => movAsset = call.Arg<MediaAsset>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().TryWriteAsync(context, CancellationToken.None);

        movAsset.Should().NotBeNull();
        movAsset.ContentHash.Should().Be("cafebabe");
        movAsset.IsSoftDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairItem_HeicPairedAssetIdPointsToMovAsync()
    {
        MediaAsset? movAsset = null;
        var fx = new TestFixture();
        fx.Repository.When(r => r.Add(Arg.Is<MediaAsset>(a => a.MediaType == MediaType.Video)))
            .Do(call => movAsset = call.Arg<MediaAsset>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.PairedAssetId.Should().Be(movAsset!.Id);
    }

    [Fact]
    public async Task TryWriteAsync_Always_UsesContextAssetIdForPrimaryAssetAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset!.Id.Should().Be(context.AssetId);
    }

    [Fact]
    public async Task TryWriteAsync_Always_AddsTheProxyFilesFromContextToTheRepositoryAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        var proxyFile = new ProxyFile { Id = Guid.NewGuid(), AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail };
        context.AddProxyFile(proxyFile);

        await fx.Build().TryWriteAsync(context, CancellationToken.None);

        fx.Repository.Received(1).AddProxyFile(proxyFile);
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairItem_UsesGuidFactoryForSecondaryAssetIdAsync()
    {
        var fx = new TestFixture();
        var expectedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(expectedId);
        MediaAsset? movAsset = null;
        fx.Repository.When(r => r.Add(Arg.Is<MediaAsset>(a => a.MediaType == MediaType.Video)))
            .Do(call => movAsset = call.Arg<MediaAsset>());
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        await fx.Build().TryWriteAsync(context, CancellationToken.None);

        movAsset!.Id.Should().Be(expectedId);
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairWithMovAlreadyPersisted_PairsHeicToExistingMovAsync()
    {
        var fx = new TestFixture();
        var existingMov = new MediaAsset
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ContentHash = "cafebabe",
            ProxyFiles = [new ProxyFile { ProxyType = ProxyType.WebVideo }],
        };
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"),
            storedMov: existingMov);

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        fx.Repository.Received(1).Add(Arg.Any<MediaAsset>());
        fx.Repository.Received(1).Add(Arg.Is<MediaAsset>(a => a.Id == context.AssetId));
        asset!.PairedAssetId.Should().Be(existingMov.Id);
        await fx.MovGenerator.DidNotReceive().TranscodeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.Repository.DidNotReceive().AddProxyFile(Arg.Any<ProxyFile>());
    }

    [Fact]
    public async Task TryWriteAsync_LivePhotoPairItem_TranscodesTheMovAsTheMovsOwnProxyAsync()
    {
        var fx = new TestFixture();
        var movId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(movId, Guid.Parse("99999999-9999-9999-9999-999999999999"));
        var added = new List<ProxyFile>();
        fx.Repository.When(r => r.AddProxyFile(Arg.Any<ProxyFile>())).Do(call => added.Add(call.Arg<ProxyFile>()));
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        await fx.MovGenerator.Received(1).TranscodeAsync("/abs/photo.mov", Arg.Any<string>(), Arg.Any<CancellationToken>());
        var row = added.Should().ContainSingle().Which;
        row.AssetId.Should().Be(movId);
        row.ProxyType.Should().Be(ProxyType.WebVideo);
        asset!.ProxyFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task TryWriteAsync_Always_AddsOnlyTheAssetsOwnProxyFilesAsync()
    {
        var fx = new TestFixture();
        var added = new List<ProxyFile>();
        fx.Repository.When(r => r.AddProxyFile(Arg.Any<ProxyFile>())).Do(call => added.Add(call.Arg<ProxyFile>()));
        var context = MakeContext();
        var own = new ProxyFile { Id = Guid.NewGuid(), AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail };
        context.AddProxyFile(own);
        context.AddProxyFile(new ProxyFile { Id = Guid.NewGuid(), AssetId = Guid.NewGuid(), ProxyType = ProxyType.WebVideo });

        await fx.Build().TryWriteAsync(context, CancellationToken.None);

        added.Should().ContainSingle().Which.Should().BeSameAs(own);
    }

    // ==========================================================================
    // TryWriteAsync — unique violations
    // ==========================================================================

    [Fact]
    public async Task TryWriteAsync_ActiveHashConflictWithPersistedPrimary_SkipsWithoutThrowingAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("deadbeef", context.Config.Id, Arg.Any<CancellationToken>())
            .Returns(new MediaAsset { ContentHash = "deadbeef" });

        var asset = await fx.Build().TryWriteAsync(context, CancellationToken.None);

        asset.Should().BeNull();
    }

    [Fact]
    public async Task TryWriteAsync_ActiveHashConflictWithoutPersistedPrimary_RethrowsAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MediaAsset?)null);

        var act = () => fx.Build().TryWriteAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData(MediaAssetIndexNames.ActiveContentHashUnique)]
    [InlineData(ProxyFileIndexNames.OnePerAssetAndType)]
    public async Task TryWriteAsync_PairLosesTheRaceForItsMov_ReleasesTheMovsProxiesBeforeRethrowingAsync(string constraintName)
    {
        var fx = new TestFixture();
        var movId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(movId);
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));
        var heicProxy = new ProxyFile { AssetId = context.AssetId, ProxyType = ProxyType.Thumbnail };
        context.AddProxyFile(heicProxy);
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(DatabaseErrors.UniqueViolation(constraintName));

        var act = () => fx.Build().TryWriteAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        context.ProxyFiles.Should().ContainSingle().Which.Should().BeSameAs(heicProxy);
    }

    [Fact]
    public async Task TryWriteAsync_PairFailsForAnotherReason_KeepsTheMovsProxiesForCompensationAsync()
    {
        var fx = new TestFixture();
        var movId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(movId);
        var context = MakeContext(
            item: new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"));
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath"));

        var act = () => fx.Build().TryWriteAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        context.ProxyFiles.Should().ContainSingle(p => p.AssetId == movId);
    }

    [Fact]
    public async Task TryWriteAsync_OtherUniqueViolation_RethrowsWithoutLookupAsync()
    {
        var fx = new TestFixture();
        var context = MakeContext();
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation("IX_MediaAssets_StorageConfigId_FilePath"));

        var act = () => fx.Build().TryWriteAsync(context, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        await fx.Repository.DidNotReceive()
            .FindByHashAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
