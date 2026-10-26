using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Classification;

public sealed class StoredContentTests
{
    private static readonly Instant FirstSeen = Instant.FromUtc(2025, 1, 1, 0, 0);
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 26, 9, 0);
    private static readonly Guid AssetId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IBurstRepository Bursts { get; } = Substitute.For<IBurstRepository>();
        public List<ProxyFile> Added { get; } = [];
        public List<ProxyFile> Removed { get; } = [];
        public IngestionContext Context { get; } = new()
        {
            Item = new SingleFileItem("/abs/a.jpg", "a.jpg", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AttemptId = Guid.NewGuid(),
        };

        public TestFixture()
        {
            Repository.When(r => r.AddProxyFiles(Arg.Any<IEnumerable<ProxyFile>>())).Do(call => Added.AddRange(call.Arg<IEnumerable<ProxyFile>>()));
            Repository.When(r => r.RemoveProxyFile(Arg.Any<ProxyFile>())).Do(call => Removed.Add(call.Arg<ProxyFile>()));
        }

        public StoredContent Build()
        {
            var clock = Substitute.For<IClock>();
            clock.GetCurrentInstant().Returns(Now);
            return new StoredContent(Repository, Bursts, clock);
        }
    }

    private static MediaAsset Row(bool softDeleted = false) => new()
    {
        Id = AssetId,
        FilePath = "a.jpg",
        FileName = "a.jpg",
        ContentHash = "0ld",
        MediaType = MediaType.Image,
        DateCaptured = new LocalDateTime(2019, 3, 4, 5, 6),
        IsSoftDeleted = softDeleted,
        LastSeenOnNas = FirstSeen,
        BurstId = Guid.NewGuid(),
    };

    private static ProxyFile Proxy(ProxyType type, string path) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = AssetId,
        ProxyType = type,
        ProxyPath = path,
        SizeBytes = 1,
        CreatedAt = FirstSeen,
    };

    private static AssetContent NewContent(params ProxyFile[] proxies) => new(
        "n3w", new LocalDateTime(2024, 7, 15, 12, 0), MediaType.LivePhoto, new Metadata { Width = 640, CameraMake = "Fujifilm" }, proxies);

    // ==========================================================================
    // TakeAsync
    // ==========================================================================

    [Fact]
    public async Task TakeAsync_ActiveRow_TakesTheContentLeavesItsBurstAndCountsNoRestoreAsync()
    {
        var fx = new TestFixture();
        var asset = Row();

        var wasSoftDeleted = await fx.Build().TakeAsync(fx.Context, asset, NewContent(), CancellationToken.None);

        wasSoftDeleted.Should().BeFalse();
        asset.Should().BeEquivalentTo(new
        {
            ContentHash = "n3w",
            DateCaptured = new LocalDateTime(2024, 7, 15, 12, 0),
            MediaType = MediaType.LivePhoto,
            BurstId = (Guid?)null,
            IsSoftDeleted = false,
            LastSeenOnNas = Now,
        });
        fx.Context.StagedAutoRestored.Should().Be(0);
        await fx.Bursts.Received(1).RemoveCheckAsync(AssetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TakeAsync_SoftDeletedRow_IsRestoredAndCountedOnceAsync()
    {
        var fx = new TestFixture();
        var asset = Row(softDeleted: true);

        var wasSoftDeleted = await fx.Build().TakeAsync(fx.Context, asset, NewContent(), CancellationToken.None);

        wasSoftDeleted.Should().BeTrue();
        asset.IsSoftDeleted.Should().BeFalse();
        fx.Context.StagedAutoRestored.Should().Be(1);
    }

    [Fact]
    public async Task TakeAsync_RowWithoutMetadata_GetsANewRowWithTheValuesAsync()
    {
        var fx = new TestFixture();
        var asset = Row();

        await fx.Build().TakeAsync(fx.Context, asset, NewContent(), CancellationToken.None);

        asset.Metadata.Should().BeEquivalentTo(new { AssetId, Width = 640, CameraMake = "Fujifilm" });
    }

    [Fact]
    public async Task TakeAsync_StoredMetadata_IsLoadedAndUpdatedInPlaceAsync()
    {
        var fx = new TestFixture();
        var asset = Row();
        var stored = new Metadata { AssetId = AssetId, Width = 10, CameraMake = "Old" };
        fx.Repository.LoadMetadataAsync(asset, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            asset.Metadata = stored;
            return Task.CompletedTask;
        });

        await fx.Build().TakeAsync(fx.Context, asset, NewContent(), CancellationToken.None);

        asset.Metadata.Should().BeSameAs(stored);
        stored.Should().BeEquivalentTo(new { AssetId, Width = 640, CameraMake = "Fujifilm" });
    }

    [Fact]
    public async Task TakeAsync_ReplacesTheProxyRowsByTypeAsync()
    {
        var fx = new TestFixture();
        var asset = Row();
        var oldVideo = Proxy(ProxyType.WebVideo, "0l/d/video_720p.mp4");
        asset.ProxyFiles = [oldVideo];
        var thumbnail = Proxy(ProxyType.Thumbnail, "n3/w/thumbnail.jpg");

        await fx.Build().TakeAsync(fx.Context, asset, NewContent(thumbnail), CancellationToken.None);

        fx.Added.Should().Equal(thumbnail);
        fx.Removed.Should().Equal(oldVideo);
    }

    // ==========================================================================
    // ReactivateAsync
    // ==========================================================================

    [Fact]
    public async Task ReactivateAsync_SoftDeletedRow_IsActiveSeenNowUncheckedAndCountedAsync()
    {
        var fx = new TestFixture();
        var asset = Row(softDeleted: true);
        var burstId = asset.BurstId;

        var wasSoftDeleted = await fx.Build().ReactivateAsync(fx.Context, asset, CancellationToken.None);

        wasSoftDeleted.Should().BeTrue();
        asset.Should().BeEquivalentTo(new { IsSoftDeleted = false, LastSeenOnNas = Now, BurstId = burstId, ContentHash = "0ld" });
        fx.Context.StagedAutoRestored.Should().Be(1);
        await fx.Bursts.Received(1).RemoveCheckAsync(AssetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReactivateAsync_ActiveRow_CountsNothingAsync()
    {
        var fx = new TestFixture();

        var wasSoftDeleted = await fx.Build().ReactivateAsync(fx.Context, Row(), CancellationToken.None);

        wasSoftDeleted.Should().BeFalse();
        fx.Context.StagedAutoRestored.Should().Be(0);
    }
}
