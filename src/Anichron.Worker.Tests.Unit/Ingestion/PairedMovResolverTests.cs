using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

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

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public MockFileSystem FileSystem { get; } = new();
        public FakeLogger<PairedMovResolver> Logger { get; } = new();
        public List<MediaAsset> Added { get; } = [];

        public TestFixture()
        {
            Repository.When(r => r.Add(Arg.Any<MediaAsset>())).Do(call => Added.Add(call.Arg<MediaAsset>()));
            FileSystem.AddFile("/abs/A.mov", new MockFileData([])
            {
                LastWriteTime = new DateTimeOffset(new DateTime(2021, 3, 5, 9, 0, 0, DateTimeKind.Local)),
            });
        }

        public void PhotosOfTheVideo(params MediaAsset[] photos)
            => Repository.FindPhotosOfAsync(VideoId, Arg.Any<CancellationToken>()).Returns(photos);

        public Task<MediaAsset> PairAsync(MediaAsset photo, MediaAsset? storedVideo)
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            var guids = Substitute.For<IGuidFactory>();
            guids.NewGuid().Returns(NewVideoId);
            var context = new IngestionContext
            {
                Item = new LivePhotoPairItem("/abs/A.heic", "A.heic", "/abs/A.mov", "A.mov"),
                Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
                AttemptId = Guid.NewGuid(),
                ContentHash = "b0b0",
                SecondaryHash = "b1b1",
                Classification = new(
                    new FileClassification(FileOutcome.Unchanged, photo),
                    new FileClassification(storedVideo is null ? FileOutcome.New : FileOutcome.Moved, storedVideo),
                    PairingAction.AttachMov),
            };
            return new PairedMovResolver(Repository, FileSystem, clock, guids, Logger)
                .PairAsync(context, photo, new LocalDateTime(2020, 7, 8, 9, 10), CancellationToken.None);
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

    [Fact]
    public async Task PairAsync_NoStoredVideo_AddsOneAndPointsThePhotoAtItWithoutLookingForOtherPhotosAsync()
    {
        var fx = new TestFixture();
        var photo = Row(PhotoId, "A.heic");

        var mov = await fx.PairAsync(photo, storedVideo: null);

        fx.Added.Should().ContainSingle().Which.Should().BeSameAs(mov);
        mov.Should().BeEquivalentTo(new
        {
            Id = NewVideoId,
            StorageConfigId = ConfigId,
            FilePath = "A.mov",
            FileName = "A.mov",
            ContentHash = "b1b1",
            DateCaptured = new LocalDateTime(2020, 7, 8, 9, 10),
            MediaType = MediaType.Video,
            IsSoftDeleted = false,
            LastSeenOnNas = Now,
        });
        photo.PairedAssetId.Should().Be(NewVideoId);
        await fx.Repository.DidNotReceive().FindPhotosOfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PairAsync_StoredVideo_PointsThePhotoAtItAndAddsNothingAsync()
    {
        var fx = new TestFixture();
        var (photo, video) = (Row(PhotoId, "A.heic"), Row(VideoId, "A.mov"));
        fx.PhotosOfTheVideo();

        var mov = await fx.PairAsync(photo, video);

        mov.Should().BeSameAs(video);
        photo.PairedAssetId.Should().Be(VideoId);
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
        var photo = Row(PhotoId, "A.heic", VideoId);
        fx.PhotosOfTheVideo(photo);

        await fx.PairAsync(photo, Row(VideoId, "A.mov"));

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
}
