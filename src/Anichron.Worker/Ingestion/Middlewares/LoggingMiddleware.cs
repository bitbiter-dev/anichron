using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Middlewares;

internal sealed partial class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IIngestionMiddleware
{
    public bool CanInvoke(IngestionContext context) => true;

    public IngestionStepError OnCannotInvoke(IngestionContext context) => new(string.Empty);

    public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
    {
        Log.ItemStarted(logger, context.Item.AbsolutePath);
        await next(context, ct);
        Log.ItemCompleted(logger, context.Item.AbsolutePath);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Ingesting {Path}.")]
        public static partial void ItemStarted(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Completed {Path}.")]
        public static partial void ItemCompleted(ILogger logger, string path);
    }
}
