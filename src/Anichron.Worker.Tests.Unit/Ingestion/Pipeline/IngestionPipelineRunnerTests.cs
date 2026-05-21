using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class IngestionPipelineRunnerTests
{
    private static IngestionContext MakeContext() => new()
    {
        Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        AssetId = Guid.NewGuid(),
    };

    // ==========================================================================
    // Constructor — validation
    // ==========================================================================

    [Fact]
    public void Constructor_DuplicateOrders_ThrowsInvalidOperationException()
    {
        var first = Substitute.For<IIngestionMiddleware>();
        first.Order.Returns(10);
        first.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        var second = Substitute.For<IIngestionMiddleware>();
        second.Order.Returns(10);
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);

        var act = () => new IngestionPipelineRunner(
            [first, second],
            Substitute.For<ILogger<IngestionPipelineRunner>>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*10*");
    }

    [Fact]
    public void Constructor_UniqueOrders_DoesNotThrow()
    {
        var first = Substitute.For<IIngestionMiddleware>();
        first.Order.Returns(10);
        first.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        var second = Substitute.For<IIngestionMiddleware>();
        second.Order.Returns(20);
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);

        var act = () => new IngestionPipelineRunner(
            [first, second],
            Substitute.For<ILogger<IngestionPipelineRunner>>());

        act.Should().NotThrow();
    }

    // ==========================================================================
    // RunAsync — ordering
    // ==========================================================================

    [Fact]
    public async Task RunAsync_MiddlewaresRunInAscendingOrderAsync()
    {
        var invoked = new List<int>();
        var low = new StubMiddleware(10, invoked);
        var high = new StubMiddleware(20, invoked);

        // Intentionally register in reverse to verify sort
        var runner = new IngestionPipelineRunner(
            [high, low],
            Substitute.For<ILogger<IngestionPipelineRunner>>());

        await runner.RunAsync(MakeContext(), CancellationToken.None);

        invoked.Should().Equal(10, 20);
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private sealed class StubMiddleware(int order, List<int> tracker) : IIngestionMiddleware
    {
        public int Order => order;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            tracker.Add(order);
            await next(context, ct);
        }
    }
}
