namespace Anichron.Worker.Ingestion.Pipeline;

internal interface IIngestionPipelineRunner
{
    Task RunAsync(IngestionContext context, CancellationToken ct);
}

internal sealed class IngestionPipelineRunner(
    IEnumerable<IIngestionMiddleware> middlewares,
    ILogger<IngestionPipelineRunner> logger) : IIngestionPipelineRunner
{
    private readonly IngestionDelegate pipeline = IngestionPipelineBuilder.Build([.. middlewares], logger);

    public Task RunAsync(IngestionContext context, CancellationToken ct) => pipeline(context, ct);
}
