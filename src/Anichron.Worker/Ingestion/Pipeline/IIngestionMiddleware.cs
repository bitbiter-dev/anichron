namespace Anichron.Worker.Ingestion.Pipeline;

internal delegate Task IngestionDelegate(IngestionContext context, CancellationToken ct);

internal interface IIngestionMiddleware
{
    string StepName => GetType().Name;
    int Order { get; }
    Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct);
}

internal static class IngestionOrder
{
    public const int Logging = 0;
    public const int ContentHashing = 10;
    public const int IdempotencyCheck = 20;
    public const int ExifExtraction = 30;
    public const int ImageProxy = 40;
    public const int Persistence = 50;
}
