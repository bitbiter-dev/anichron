using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class LivePhotoPairingMiddlewareTests
{
    private sealed class TestFixture
    {
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();
        public IClock Clock { get; } = Substitute.For<IClock>();

        public LivePhotoPairingMiddleware Build()
            => new(Repository, UnitOfWork,
                new PairedMovResolver(Repository, new MockFileSystem(), Clock, GuidFactory),
                Substitute.For<ILogger<LivePhotoPairingMiddleware>>());
    }

    private static readonly LocalDateTime HeicCaptured = new(2023, 6, 15, 10, 0);

    private static IngestionContext MakePairContext() => new()
    {
        Item = new LivePhotoPairItem("/abs/photo.heic", "photo.heic", "/abs/photo.mov", "photo.mov"),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        AssetId = Guid.NewGuid(),
        ContentHash = "deadbeef",
        SecondaryHash = "cafebabe",
    };

    private static MediaAsset StoredHeic(Guid? pairedAssetId = null) => new()
    {
        Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        ContentHash = "deadbeef",
        MediaType = MediaType.Image,
        DateCaptured = HeicCaptured,
        PairedAssetId = pairedAssetId,
    };

    private static Task NoOpNextAsync(IngestionContext ctx, CancellationToken ct)
    {
        _ = (ctx, ct);
        return Task.CompletedTask;
    }

    // ==========================================================================
    // InvokeAsync
    // ==========================================================================

    [Fact]
    public async Task InvokeAsync_StoredHeicWithoutMov_AttachesNewMovAndStopsAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext();
        var heic = StoredHeic();
        var movId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        fx.GuidFactory.NewGuid().Returns(movId);
        fx.Repository.FindByHashAsync("deadbeef", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        MediaAsset? addedMov = null;
        fx.Repository.When(r => r.Add(Arg.Any<MediaAsset>())).Do(call => addedMov = call.Arg<MediaAsset>());
        var nextCalled = false;

        await fx.Build().InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        addedMov.Should().NotBeNull();
        addedMov.Id.Should().Be(movId);
        addedMov.ContentHash.Should().Be("cafebabe");
        addedMov.FilePath.Should().Be("photo.mov");
        addedMov.DateCaptured.Should().Be(HeicCaptured);
        heic.MediaType.Should().Be(MediaType.LivePhoto);
        heic.PairedAssetId.Should().Be(movId);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_StoredHeicAndStoredMov_PairsToExistingMovWithoutAddingAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext();
        var heic = StoredHeic();
        var existingMov = new MediaAsset { Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), ContentHash = "cafebabe" };
        fx.Repository.FindByHashAsync("deadbeef", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        fx.Repository.FindByHashAsync("cafebabe", context.Config.Id, Arg.Any<CancellationToken>()).Returns(existingMov);

        await fx.Build().InvokeAsync(context, NoOpNextAsync, CancellationToken.None);

        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
        heic.PairedAssetId.Should().Be(existingMov.Id);
        heic.MediaType.Should().Be(MediaType.LivePhoto);
        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_StoredHeicAlreadyPaired_PassesThroughUnchangedAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext();
        var pairedTo = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var heic = StoredHeic(pairedTo);
        fx.Repository.FindByHashAsync("deadbeef", context.Config.Id, Arg.Any<CancellationToken>()).Returns(heic);
        var nextCalled = false;

        await fx.Build().InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        heic.PairedAssetId.Should().Be(pairedTo);
        heic.MediaType.Should().Be(MediaType.Image);
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_HeicNotStored_PassesThroughWithoutSavingAsync()
    {
        var fx = new TestFixture();
        var context = MakePairContext();
        var nextCalled = false;

        await fx.Build().InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        fx.Repository.DidNotReceive().Add(Arg.Any<MediaAsset>());
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CanInvoke_SingleFileItem_IsFalse()
    {
        var context = new IngestionContext
        {
            Item = new SingleFileItem("/abs/photo.heic", "photo.heic", MediaType.Image),
            Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
            AssetId = Guid.NewGuid(),
        };

        new TestFixture().Build().CanInvoke(context).Should().BeFalse();
    }

    [Fact]
    public void CanInvoke_LivePhotoPairItem_IsTrue()
        => new TestFixture().Build().CanInvoke(MakePairContext()).Should().BeTrue();
}
