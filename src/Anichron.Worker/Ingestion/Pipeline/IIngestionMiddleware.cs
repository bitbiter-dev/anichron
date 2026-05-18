namespace Anichron.Worker.Ingestion.Pipeline;

internal delegate Task IngestionDelegate(IngestionContext context, CancellationToken ct);

internal interface IIngestionMiddleware
{
    bool CanInvoke(IngestionContext context);
    IngestionStepError OnCannotInvoke(IngestionContext context);
    Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct);
}

internal sealed record IngestionStepError(string Message);
