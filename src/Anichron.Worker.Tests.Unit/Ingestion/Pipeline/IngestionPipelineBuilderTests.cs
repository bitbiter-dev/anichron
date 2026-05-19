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

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            tracker.Add(index);
            await next(context, ct);
        }
    }
}
