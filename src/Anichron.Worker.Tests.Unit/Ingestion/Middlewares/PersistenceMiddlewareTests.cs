using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Tests.Unit.Ingestion.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute.ExceptionExtensions;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class PersistenceMiddlewareTests
{
    private static readonly Guid AssetId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ConfigId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class TestFixture
    {
        public IOutcomeWriter NewWriter { get; } = Writer(FileOutcome.New);
        public IOutcomeWriter OtherWriter { get; } = Writer(FileOutcome.Moved);
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IMediaAssetRepository Repository { get; } = Substitute.For<IMediaAssetRepository>();
        public FakeLogger<PersistenceMiddleware> Logger { get; } = new();
        public bool NextCalled { get; private set; }

        public Task InvokeAsync(IngestionContext context)
            => new PersistenceMiddleware([OtherWriter, NewWriter], new IngestionSaver(UnitOfWork, Repository), Logger)
                .InvokeAsync(context, (_, _) =>
                {
                    NextCalled = true;
                    return Task.CompletedTask;
                }, CancellationToken.None);

        private static IOutcomeWriter Writer(FileOutcome outcome)
        {
            var writer = Substitute.For<IOutcomeWriter>();
            writer.Outcomes.Returns([outcome]);
            writer.WriteAsync(Arg.Any<IngestionContext>(), Arg.Any<CancellationToken>())
                .Returns(new MediaAsset { Id = AssetId, ContentHash = "deadbeef", StorageConfigId = ConfigId });
            return writer;
        }
    }

    private static IngestionContext Context(FileOutcome outcome) => new()
    {
        Item = new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = ConfigId, UserId = Guid.NewGuid(), RootPath = "/abs" },
        AttemptId = Guid.NewGuid(),
        AssetId = AssetId,
        Classification = Classifications.Of(outcome),
    };

    [Fact]
    public async Task InvokeAsync_UsesTheWriterForThePrimaryOutcomeAsync()
    {
        var fx = new TestFixture();
        var context = Context(FileOutcome.New);

        await fx.InvokeAsync(context);

        await fx.NewWriter.Received(1).WriteAsync(context, Arg.Any<CancellationToken>());
        await fx.OtherWriter.DidNotReceive().WriteAsync(Arg.Any<IngestionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_NoWriterForTheOutcome_ThrowsWithoutSavingOrCallingNextAsync()
    {
        var fx = new TestFixture();

        var act = () => fx.InvokeAsync(Context(FileOutcome.Unchanged));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        fx.NextCalled.Should().BeFalse();
        await fx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_Written_SavesTheItemOnceAsync()
    {
        var fx = new TestFixture();

        await fx.InvokeAsync(Context(FileOutcome.New));

        await fx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_Saved_SetsTheAssetLogsAndCallsNextAsync()
    {
        var fx = new TestFixture();
        var context = Context(FileOutcome.New);

        await fx.InvokeAsync(context);

        context.Asset.Should().NotBeNull().And.Match<MediaAsset>(asset => asset.Id == AssetId);
        fx.NextCalled.Should().BeTrue();
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(r => r.Level == LogLevel.Information
                && r.Message == "Persisted photo.jpg as asset eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    }

    [Fact]
    public async Task InvokeAsync_ContentCommittedConcurrently_StopsWithoutAnAssetAsync()
    {
        var fx = new TestFixture();
        var context = Context(FileOutcome.New);
        fx.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique));
        fx.Repository.FindByHashAsync("deadbeef", ConfigId, Arg.Any<CancellationToken>())
            .Returns(new MediaAsset { ContentHash = "deadbeef" });

        await fx.InvokeAsync(context);

        context.Asset.Should().BeNull();
        fx.NextCalled.Should().BeFalse();
        fx.Logger.Collector.GetSnapshot().Should().ContainSingle()
            .Which.Should().Match<FakeLogRecord>(r => r.Level == LogLevel.Debug
                && r.Message == "Content of photo.jpg was persisted concurrently by another consumer, skipping.");
    }
}
