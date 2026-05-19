namespace Anichron.Worker.Ingestion.Pipeline;

internal interface IIngestionPipelineRunner
{
    Task RunAsync(IngestionContext context, CancellationToken ct);
}

internal sealed class IngestionPipelineRunner(
    IEnumerable<IIngestionMiddleware> middlewares,
    ILogger<IngestionPipelineRunner> logger) : IIngestionPipelineRunner
{
    private readonly IngestionDelegate pipeline = BuildValidated([.. middlewares], logger);

    public Task RunAsync(IngestionContext context, CancellationToken ct) => pipeline(context, ct);

    private static IngestionDelegate BuildValidated(
        IReadOnlyList<IIngestionMiddleware> middlewares, ILogger logger)
    {
        Validate(middlewares);
        var ordered = middlewares.OrderBy(m => m.Order).ToArray();
        return IngestionPipelineBuilder.Build(ordered, logger);
    }

    private static void Validate(IReadOnlyList<IIngestionMiddleware> middlewares)
    {
        var duplicates = middlewares
            .GroupBy(m => m.Order)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate middleware orders: {string.Join(", ", duplicates)}");
        }
    }
}
