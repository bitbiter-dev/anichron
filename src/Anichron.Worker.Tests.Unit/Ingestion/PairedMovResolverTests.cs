using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class PairedMovResolverTests
{
    private static readonly Instant FirstSeen = Instant.FromUtc(2025, 1, 1, 0, 0);
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 29, 9, 0);
    private static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PhotoId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000000");
    private static readonly Guid VideoId = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000000");
    private static readonly Guid PreviousId = Guid.Parse("b1b1b1b1-0000-0000-0000-000000000000");
    private static readonly Guid NewVideoId = Guid.Parse("cccccccc-0000-0000-0000-000000000000");
    private static readonly Guid LetGoId = Guid.Parse("b3b3b3b3-0000-0000-0000-000000000000");

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public FakeLogger<PairedMovResolver> Logger { get; } = new();
        public List<MediaAsset> Added { get; } = [];
        public IngestionContext? Context { get; private set; }

        public TestFixture()
            => Repository.When(r => r.Add(Arg.Any<MediaAsset>())).Do(call => Added.Add(call.Arg<MediaAsset>()));

        public void PhotosOfTheVideo(params MediaAsset[] photos)
            => Repository.FindPhotosOfAsync(VideoId, Arg.Any<CancellationToken>()).Returns(photos);

        public void Stored(MediaAsset row)
            => Repository.FindAsync(row.Id, Arg.Any<CancellationToken>()).Returns(row);

        public Task<MediaAsset> PairAsync(
            MediaAsset photo, MediaAsset? storedVideo, FileOutcome videoOutcome = FileOutcome.Moved, MediaAsset? retiredByTheVideo = null)
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            var guids = Substitute.For<IGuidFactory>();
            guids.NewGuid().Returns(NewVideoId);
            Context = new IngestionContext
            {
                Item = new LivePhotoPairItem("/abs/A.heic", "A.heic", "/abs/A.mov", "A.mov"),
                Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
                AttemptId = Guid.NewGuid(),
                ContentHash = "b0b0",
                SecondaryHash = "b1b1",
                Classification = new(
                    new FileClassification(FileOutcome.Unchanged, photo),
                    new FileClassification(storedVideo is null ? FileOutcome.New : videoOutcome, storedVideo, retiredByTheVideo),
                    PairingAction.AttachMov),
            };
            var storedContent = new StoredContent(Repository, Substitute.For<IBurstRepository>(), clock, new FakeLogger<StoredContent>());
            return new PairedMovResolver(Repository, storedContent, clock, guids, Logger)
                .PairAsync(Context, photo, CancellationToken.None);
        }
    }

    private static MediaAsset Row(Guid id, string path, Guid? pairedAssetId = null, bool softDeleted = false) => new()
    {
        Id = id,
        StorageConfigId = ConfigId,
        FilePath = path,
        FileName = path,
        ContentHash = path,
        PairedAssetId = pairedAssetId,
        IsSoftDeleted = softDeleted,
        LastSeenOnNas = FirstSeen,
    };

    private static CaptureDate InAuckland => new(
        new LocalDateTime(2020, 7, 8, 9, 10), Offset.FromHours(12), CaptureSource.Recorded, "Pacific/Auckland");

    [Fact]
    public async Task PairAsync_NoStoredVideo_AddsOneAndPointsThePhotoAtItWithoutLookingForOtherPhotosAsync()
    {
        var fx = new TestFixture();
        var photo = Row(PhotoId, "A.heic");
        photo.Captured = InAuckland;

        var mov = await fx.PairAsync(photo, storedVideo: null);

        fx.Added.Should().ContainSingle().Which.Should().BeSameAs(mov);
        mov.Should().BeEquivalentTo(new
        {
            Id = NewVideoId,
            StorageConfigId = ConfigId,
            FilePath = "A.mov",
            FileName = "A.mov",
            ContentHash = "b1b1",
            Captured = InAuckland,
            MediaType = MediaType.Video,
            IsSoftDeleted = false,
            IsLivePhotoVideo = true,
            LastSeenOnNas = Now,
        });
        photo.PairedAssetId.Should().Be(NewVideoId);
        await fx.Repository.DidNotReceive().FindPhotosOfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PairAsync_NoStoredVideoBesideAnUndatedPhoto_AddsAnUndatedVideoAsync()
    {
        var fx = new TestFixture();

        var mov = await fx.PairAsync(Row(PhotoId, "A.heic"), storedVideo: null);

        mov.Captured.Should().BeNull();
    }

    [Theory]
    [InlineData((int)FileOutcome.ContentChanged)]
    [InlineData((int)FileOutcome.AutoRestoredContentChanged)]
    [InlineData((int)FileOutcome.OverwrittenByMove)]
    public async Task PairAsync_StoredVideoTakingContent_TakesThePhotosDateAsync(int videoOutcome)
    {
        var fx = new TestFixture();
        var (photo, video) = (Row(PhotoId, "A.heic"), Row(VideoId, "A.mov"));
        photo.Captured = InAuckland;
        video.Captured = Recorded.At(new LocalDateTime(2019, 1, 2, 3, 4));
        fx.PhotosOfTheVideo();

        await fx.PairAsync(photo, video, (FileOutcome)videoOutcome);

        video.Captured.Should().Be(InAuckland);
    }

    [Fact]
    public async Task PairAsync_StoredVideoKeepingItsContent_KeepsItsDateAsync()
    {
        var fx = new TestFixture();
        var (photo, video) = (Row(PhotoId, "A.heic"), Row(VideoId, "A.mov"));
        photo.Captured = InAuckland;
        video.Captured = Recorded.At(new LocalDateTime(2019, 1, 2, 3, 4));
        fx.PhotosOfTheVideo();

        await fx.PairAsync(photo, video, FileOutcome.Unchanged);

        video.Captured.Should().Be(Recorded.At(new LocalDateTime(2019, 1, 2, 3, 4)));
    }

    [Fact]
    public async Task PairAsync_StoredVideo_PointsThePhotoAtItAndAddsNothingAsync()
    {
        var fx = new TestFixture();
        var (photo, video) = (Row(PhotoId, "A.heic"), Row(VideoId, "A.mov"));
        fx.PhotosOfTheVideo();

        var mov = await fx.PairAsync(photo, video);

        mov.Should().BeSameAs(video);
        (photo.PairedAssetId, video.IsLivePhotoVideo).Should().Be((VideoId, true));
        fx.Added.Should().BeEmpty();
        fx.Logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public async Task PairAsync_VideoAnotherActivePhotoPointsAt_TakesItFromThatPhotoAndSaysItIsNowAStillPhotoAsync()
    {
        var fx = new TestFixture();
        var (photo, video, previous) = (Row(PhotoId, "A.heic"), Row(VideoId, "B.mov"), Row(PreviousId, "B.heic", VideoId));
        fx.PhotosOfTheVideo(previous);

        await fx.PairAsync(photo, video);

        (photo.PairedAssetId, previous.PairedAssetId).Should().Be((VideoId, null));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Level = LogLevel.Information,
            Message = "Asset b1b1b1b1-0000-0000-0000-000000000000 is now a still photo: its video, asset "
                + "b2b2b2b2-0000-0000-0000-000000000000, is now the video of asset a1a1a1a1-0000-0000-0000-000000000000.",
        });
    }

    [Fact]
    public async Task PairAsync_VideoASoftDeletedPhotoPointsAt_TakesItWithoutALogAsync()
    {
        var fx = new TestFixture();
        var previous = Row(PreviousId, "B.heic", VideoId, softDeleted: true);
        fx.PhotosOfTheVideo(previous);

        await fx.PairAsync(Row(PhotoId, "A.heic"), Row(VideoId, "B.mov"));

        previous.PairedAssetId.Should().BeNull();
        fx.Logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public async Task PairAsync_PhotoThatAlreadyPointsAtTheVideo_KeepsItAndIsNotCalledAStillPhotoAsync()
    {
        var fx = new TestFixture();
        var (photo, video) = (Row(PhotoId, "A.heic", VideoId), Row(VideoId, "A.mov"));
        fx.PhotosOfTheVideo(photo);
        fx.Stored(video);

        await fx.PairAsync(photo, video);

        (video.IsSoftDeleted, fx.Context!.StagedSoftDeleted).Should().Be((false, 0));

        photo.PairedAssetId.Should().Be(VideoId);
        fx.Logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public async Task PairAsync_StoredPhotoAlreadyRepointedInThisItem_KeepsItsCurrentPointerAsync()
    {
        var fx = new TestFixture();
        var elsewhere = Guid.Parse("dddddddd-0000-0000-0000-000000000000");
        var previous = Row(PreviousId, "B.heic", elsewhere);
        fx.PhotosOfTheVideo(previous);

        await fx.PairAsync(Row(PhotoId, "A.heic"), Row(VideoId, "B.mov"));

        previous.PairedAssetId.Should().Be(elsewhere);
        fx.Logger.Collector.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairAsync_PhotoWithAnotherActiveVideo_RetiresThatVideoAndCountsItAsync(bool videoStored)
    {
        var fx = new TestFixture();
        var (photo, letGo) = (Row(PhotoId, "A.heic", LetGoId), Row(LetGoId, "Z.mov"));
        fx.Stored(letGo);
        fx.PhotosOfTheVideo();

        var mov = await fx.PairAsync(photo, videoStored ? Row(VideoId, "A.mov") : null);

        (photo.PairedAssetId, letGo.IsSoftDeleted, fx.Context!.StagedSoftDeleted).Should().Be((mov.Id, true, 1));
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Level = LogLevel.Information,
            Message = "Soft-deleted asset b3b3b3b3-0000-0000-0000-000000000000 at Z.mov: its photo, asset "
                + $"a1a1a1a1-0000-0000-0000-000000000000, now has another video, asset {mov.Id}.",
        });
    }

    [Fact]
    public async Task PairAsync_PhotosPreviousVideoAlreadySoftDeleted_CountsAndLogsNothingAsync()
    {
        var fx = new TestFixture();
        fx.Stored(Row(LetGoId, "Z.mov", softDeleted: true));
        fx.PhotosOfTheVideo();

        await fx.PairAsync(Row(PhotoId, "A.heic", LetGoId), Row(VideoId, "A.mov"));

        fx.Context!.StagedSoftDeleted.Should().Be(0);
        fx.Logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public async Task PairAsync_PhotosPreviousVideoNeitherTrackedNorActive_CountsAndLogsNothingAsync()
    {
        var fx = new TestFixture();
        fx.PhotosOfTheVideo();

        await fx.PairAsync(Row(PhotoId, "A.heic", LetGoId), Row(VideoId, "A.mov"));

        fx.Context!.StagedSoftDeleted.Should().Be(0);
        fx.Logger.Collector.Count.Should().Be(0);
    }

    [Fact]
    public async Task PairAsync_PhotosPreviousVideoTheVideoHalfRetires_IsLeftToThatOutcomeAsync()
    {
        var fx = new TestFixture();
        var letGo = Row(LetGoId, "Z.mov");
        fx.Stored(letGo);
        fx.PhotosOfTheVideo();

        await fx.PairAsync(Row(PhotoId, "A.heic", LetGoId), Row(VideoId, "A.mov"), FileOutcome.OverwrittenByMove, retiredByTheVideo: letGo);

        (letGo.IsSoftDeleted, fx.Context!.StagedSoftDeleted).Should().Be((false, 0));
        fx.Logger.Collector.Count.Should().Be(0);
    }
}
