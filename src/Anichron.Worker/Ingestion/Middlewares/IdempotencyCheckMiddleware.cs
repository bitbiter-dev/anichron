using Anichron.Core.Data.Repository;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class IdempotencyCheckMiddleware(
    IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyCheckMiddleware> logger) : IIngestionMiddleware
{
    public bool CanInvoke(IngestionContext context) => context.ContentHash is not null;

    public IngestionStepError OnCannotInvoke(IngestionContext context)
        => new("ContentHash must be set before idempotency check");

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMediaAssetRepository>();
        var existing = await repository.FindByHashAsync(context.ContentHash!, ct); // CanInvoke verified not null
        if (existing is not null)
        {
            Log.AlreadyIngested(logger, context.Item.AbsolutePath);
            return;
        }

        await next(context, ct);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Already ingested {Path}, skipping.")]
        public static partial void AlreadyIngested(ILogger logger, string path);
    }
}
