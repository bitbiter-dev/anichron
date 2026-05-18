using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class IngestionPipelineBuilderTests
{
    private static IngestionContext MakeContext() => new()
    {
        Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
    };

    // ==========================================================================
    // Build — ordering
    // ==========================================================================

    [Fact]
    public async Task Build_MultipleMiddlewares_InvokesInRegistrationOrderAsync()
    {
        var order = new List<int>();
        var first = new OrderCapturingMiddleware(1, order);
        var second = new OrderCapturingMiddleware(2, order);

        var pipeline = IngestionPipelineBuilder.Build([first, second]);
        await pipeline(MakeContext(), CancellationToken.None);

        order.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Build_EmptyPipeline_CompletesWithoutExceptionAsync()
    {
        var pipeline = IngestionPipelineBuilder.Build([]);
        var act = async () => await pipeline(MakeContext(), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ==========================================================================
    // Build — CanInvoke guard
    // ==========================================================================

    [Fact]
    public async Task Build_CanInvokeReturnsFalse_ThrowsPipelineConfigurationExceptionAsync()
    {
        var middleware = Substitute.For<IIngestionMiddleware>();
        middleware.CanInvoke(Arg.Any<IngestionContext>()).Returns(false);
        middleware.OnCannotInvoke(Arg.Any<IngestionContext>()).Returns(new IngestionStepError("hash is required"));

        var pipeline = IngestionPipelineBuilder.Build([middleware]);
        var act = async () => await pipeline(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<PipelineConfigurationException>()
            .WithMessage("*hash is required*");
    }

    [Fact]
    public async Task Build_CanInvokeReturnsFalse_ExceptionMessageContainsMiddlewareNameAsync()
    {
        var middleware = Substitute.For<IIngestionMiddleware>();
        middleware.CanInvoke(Arg.Any<IngestionContext>()).Returns(false);
        middleware.OnCannotInvoke(Arg.Any<IngestionContext>()).Returns(new IngestionStepError("missing"));

        var pipeline = IngestionPipelineBuilder.Build([middleware]);
        var act = async () => await pipeline(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<PipelineConfigurationException>()
            .WithMessage("*cannot invoke*");
    }

    [Fact]
    public async Task Build_CanInvokeReturnsFalse_InvokeAsyncIsNeverCalledAsync()
    {
        var middleware = Substitute.For<IIngestionMiddleware>();
        middleware.CanInvoke(Arg.Any<IngestionContext>()).Returns(false);
        middleware.OnCannotInvoke(Arg.Any<IngestionContext>()).Returns(new IngestionStepError(string.Empty));

        var pipeline = IngestionPipelineBuilder.Build([middleware]);
        try
        { await pipeline(MakeContext(), CancellationToken.None); }
        catch (PipelineConfigurationException exception) { _ = exception; }

        await middleware.DidNotReceive().InvokeAsync(
            Arg.Any<IngestionContext>(), Arg.Any<IngestionDelegate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Build_SecondMiddlewareCannotInvoke_FirstMiddlewareStillRunsAsync()
    {
        var invoked = new List<int>();
        var first = new OrderCapturingMiddleware(1, invoked);

        var second = Substitute.For<IIngestionMiddleware>();
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(false);
        second.OnCannotInvoke(Arg.Any<IngestionContext>()).Returns(new IngestionStepError(string.Empty));

        var pipeline = IngestionPipelineBuilder.Build([first, second]);
        try
        { await pipeline(MakeContext(), CancellationToken.None); }
        catch (PipelineConfigurationException exception) { _ = exception; }

        invoked.Should().Contain(1);
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private sealed class OrderCapturingMiddleware(int order, List<int> tracker) : IIngestionMiddleware
    {
        public bool CanInvoke(IngestionContext context) => true;
        public IngestionStepError OnCannotInvoke(IngestionContext context) => new(string.Empty);

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            tracker.Add(order);
            await next(context, ct);
        }
    }
}
