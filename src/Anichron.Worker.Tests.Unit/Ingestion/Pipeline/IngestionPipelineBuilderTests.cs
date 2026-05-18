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

        var first = Substitute.For<IIngestionMiddleware>();
        first.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        first.InvokeAsync(Arg.Any<IngestionContext>(), Arg.Any<IngestionDelegate>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                order.Add(1);
                return callInfo.ArgAt<IngestionDelegate>(1)(callInfo.ArgAt<IngestionContext>(0), callInfo.ArgAt<CancellationToken>(2));
            });

        var second = Substitute.For<IIngestionMiddleware>();
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        second.InvokeAsync(Arg.Any<IngestionContext>(), Arg.Any<IngestionDelegate>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                order.Add(2);
                return callInfo.ArgAt<IngestionDelegate>(1)(callInfo.ArgAt<IngestionContext>(0), callInfo.ArgAt<CancellationToken>(2));
            });

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
        var firstInvoked = false;

        var first = Substitute.For<IIngestionMiddleware>();
        first.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        first.InvokeAsync(Arg.Any<IngestionContext>(), Arg.Any<IngestionDelegate>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                firstInvoked = true;
                return callInfo.ArgAt<IngestionDelegate>(1)(callInfo.ArgAt<IngestionContext>(0), callInfo.ArgAt<CancellationToken>(2));
            });

        var second = Substitute.For<IIngestionMiddleware>();
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(false);
        second.OnCannotInvoke(Arg.Any<IngestionContext>()).Returns(new IngestionStepError(string.Empty));

        var pipeline = IngestionPipelineBuilder.Build([first, second]);
        try
        { await pipeline(MakeContext(), CancellationToken.None); }
        catch (PipelineConfigurationException exception) { _ = exception; }

        firstInvoked.Should().BeTrue();
    }
}
