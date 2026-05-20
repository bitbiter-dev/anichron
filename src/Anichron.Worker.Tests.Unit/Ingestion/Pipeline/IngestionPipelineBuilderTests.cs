using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.Extensions.Logging;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class IngestionPipelineBuilderTests
{
    private static IngestionContext MakeContext() => new()
    {
        Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        AssetId = Guid.NewGuid(),
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

        var pipeline = IngestionPipelineBuilder.Build([first, second], Substitute.For<ILogger>());
        await pipeline(MakeContext(), CancellationToken.None);

        order.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Build_EmptyPipeline_CompletesWithoutExceptionAsync()
    {
        var pipeline = IngestionPipelineBuilder.Build([], Substitute.For<ILogger>());
        var act = async () => await pipeline(MakeContext(), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private sealed class OrderCapturingMiddleware(int index, List<int> tracker) : IIngestionMiddleware
    {
        public int Order => 0;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            tracker.Add(index);
            await next(context, ct);
        }
    }

    private sealed class ConditionalMiddleware(bool canInvoke, List<string> log) : IIngestionMiddleware
    {
        public int Order => 0;
        public bool CanInvoke(IngestionContext context) => canInvoke;

        public Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            log.Add("invoked");
            return next(context, ct);
        }
    }

    // ==========================================================================
    // Build — CanInvoke
    // ==========================================================================

    [Fact]
    public async Task Build_WhenCanInvokeReturnsFalse_SkipsInvokeAsyncAndCallsNextAsync()
    {
        var log = new List<string>();
        var skipped = new ConditionalMiddleware(canInvoke: false, log);

        var pipeline = IngestionPipelineBuilder.Build([skipped], Substitute.For<ILogger>());
        await pipeline(MakeContext(), CancellationToken.None);

        log.Should().BeEmpty();
    }

    [Fact]
    public async Task Build_WhenCanInvokeReturnsFalse_SubsequentMiddlewaresStillRunAsync()
    {
        var log = new List<string>();
        var skipped = new ConditionalMiddleware(canInvoke: false, log);
        var executed = new ConditionalMiddleware(canInvoke: true, log);

        var pipeline = IngestionPipelineBuilder.Build([skipped, executed], Substitute.For<ILogger>());
        await pipeline(MakeContext(), CancellationToken.None);

        log.Should().Equal("invoked");
    }
}
