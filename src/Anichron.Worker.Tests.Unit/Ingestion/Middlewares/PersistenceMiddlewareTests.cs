using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Middlewares;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Tests.Unit.Ingestion.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Anichron.Worker.Tests.Unit.Ingestion.Middlewares;

public sealed class PersistenceMiddlewareTests
{
    private static readonly Guid AssetId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private sealed class TestFixture
    {
        public IOutcomeWriter NewWriter { get; } = Writer(FileOutcome.New);
        public IOutcomeWriter OtherWriter { get; } = Writer(FileOutcome.Moved);
        public FakeLogger<PersistenceMiddleware> Logger { get; } = new();
        public bool NextCalled { get; private set; }

        public Task InvokeAsync(IngestionContext context)
            => new PersistenceMiddleware([OtherWriter, NewWriter], Logger).InvokeAsync(context, (_, _) =>
            {
                NextCalled = true;
                return Task.CompletedTask;
            }, CancellationToken.None);

        private static IOutcomeWriter Writer(FileOutcome outcome)
        {
            var writer = Substitute.For<IOutcomeWriter>();
            writer.Outcomes.Returns([outcome]);
            return writer;
        }
    }

    private static IngestionContext Context(FileOutcome outcome) => new()
    {
        Item = new SingleFileItem("/abs/photo.jpg", "photo.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
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

        await fx.NewWriter.Received(1).TryWriteAsync(context, Arg.Any<CancellationToken>());
        await fx.OtherWriter.DidNotReceive().TryWriteAsync(Arg.Any<IngestionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_NoWriterForTheOutcome_ThrowsWithoutCallingNextAsync()
    {
        var fx = new TestFixture();

        var act = () => fx.InvokeAsync(Context(FileOutcome.Unchanged));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        fx.NextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Written_SetsTheAssetLogsAndCallsNextAsync()
    {
        var fx = new TestFixture();
        var context = Context(FileOutcome.New);
        var asset = new MediaAsset { Id = AssetId };
        fx.NewWriter.TryWriteAsync(context, Arg.Any<CancellationToken>()).Returns(asset);

        await fx.InvokeAsync(context);

        context.Asset.Should().BeSameAs(asset);
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
        fx.NewWriter.TryWriteAsync(context, Arg.Any<CancellationToken>()).Returns((MediaAsset?)null);

        await fx.InvokeAsync(context);

        context.Asset.Should().BeNull();
        fx.NextCalled.Should().BeFalse();
    }
}
