using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

internal sealed class ClassificationFixture
{
    public static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Instant FirstSeen = Instant.FromUtc(2025, 1, 1, 0, 0, 0);
    public static readonly Instant Now = Instant.FromUtc(2026, 5, 19, 10, 0, 0);

    public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
    public IBurstRepository Bursts { get; } = Substitute.For<IBurstRepository>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public MockFileSystem FileSystem { get; } = new();
    public IClock Clock { get; } = Substitute.For<IClock>();
    public IGuidFactory GuidFactory { get; } = Substitute.For<IGuidFactory>();
    public IVideoProxyGenerator MovGenerator { get; } = Substitute.For<IVideoProxyGenerator>();
    public FakeLogger<KnownFileReconciler> Logger { get; } = new();
    public FakeLogger<ClassificationMiddleware> MiddlewareLogger { get; } = new();
    public List<ProxyFile> AddedProxies { get; } = [];

    public ClassificationFixture()
    {
        Clock.GetCurrentInstant().Returns(Now);
        UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task>>()());
        Store();
    }

    public void Store(params MediaAsset[] rows)
        => StoreMatched([.. rows.Select(row => new MatchedAsset(row, IsLivePhotoVideo: false))]);

    public void StoreMatched(params MatchedAsset[] rows)
    {
        Repository.FindByHashesOrPathsAsync(
                ConfigId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(rows);
        var tracked = rows.Select(row => row.Asset).ToList();
        var storedPointers = tracked.Where(row => row.PairedAssetId is not null).ToLookup(row => row.PairedAssetId!.Value);
        Repository.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => tracked.FirstOrDefault(row => row.Id == call.Arg<Guid>()));
        Repository.FindPhotosOfAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => [.. storedPointers[call.Arg<Guid>()]]);
    }

    public ClassificationMiddleware Build()
    {
        MovGenerator.FileName.Returns("video_720p.mp4");
        MovGenerator.ProxyType.Returns(ProxyType.WebVideo);
        MovGenerator.TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => FileSystem.File.WriteAllBytesAsync(call.ArgAt<string>(1), [0x01]));
        Repository.When(r => r.AddProxyFiles(Arg.Any<IEnumerable<ProxyFile>>())).Do(call => AddedProxies.AddRange(call.Arg<IEnumerable<ProxyFile>>()));

        var saver = new IngestionSaver(UnitOfWork, Repository);
        var stagingWriter = new ProxyStagingWriter(FileSystem, Clock, GuidFactory, new FakeLogger<ProxyStagingWriter>());
        var movProxyWriter = new PairedMovProxyWriter(
            [MovGenerator], new TwoLevelHexShardStrategy(), stagingWriter, Repository, new FakeLogger<PairedMovProxyWriter>());
        var pairer = new LivePhotoPairer(
            new PairedMovResolver(Repository, FileSystem, Clock, GuidFactory, new FakeLogger<PairedMovResolver>()), movProxyWriter, new FakeLogger<LivePhotoPairer>());
        return new(
            new IngestionClassifier(Repository, FileSystem),
            pairer,
            new KnownFileReconciler(
                Clock, Repository, new StoredContent(Repository, Bursts, Clock, new FakeLogger<StoredContent>()), GuidFactory, movProxyWriter, FileSystem, Logger),
            saver,
            MiddlewareLogger);
    }

    public Task DidNotTranscodeAsync()
        => MovGenerator.DidNotReceive().TranscodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

    public static IngestionContext Single(string relativePath = "photo.jpg") => new()
    {
        Item = new SingleFileItem($"/abs/{relativePath}", relativePath, MediaType.Image),
        Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
        AttemptId = Guid.NewGuid(),
        ContentHash = "abc123",
    };

    public static IngestionContext Pair(string heicPath = "photo.heic", string movPath = "photo.mov") => new()
    {
        Item = new LivePhotoPairItem($"/abs/{heicPath}", heicPath, $"/abs/{movPath}", movPath),
        Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
        AttemptId = Guid.NewGuid(),
        ContentHash = "abc123",
        SecondaryHash = "cafebabe",
    };

    public static Task NoOpNextAsync(IngestionContext ctx, CancellationToken ct)
    {
        _ = (ctx, ct);
        return Task.CompletedTask;
    }
}
