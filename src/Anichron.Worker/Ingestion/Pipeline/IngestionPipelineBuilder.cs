namespace Anichron.Worker.Ingestion.Pipeline;

internal static partial class IngestionPipelineBuilder
{
    internal static IngestionDelegate Build(IReadOnlyList<IIngestionMiddleware> middlewares, ILogger logger)
    {
        IngestionDelegate pipeline = static (_, _) => Task.CompletedTask;

        // Build right-to-left so the composed chain runs left-to-right:
        // wrapping middleware[n-1] first means middleware[0] is the outermost caller.
        foreach (var middleware in middlewares.Reverse())
        {
            // Capture 'pipeline' before reassignment — without this all lambdas would
            // close over the final value of the outer variable, not this iteration's.
            var next = pipeline;
            pipeline = async (context, ct) =>
            {
                if (!middleware.CanInvoke(context))
                {
                    Log.StepSkipped(logger, middleware.StepName);
                    await next(context, ct);
                    return;
                }

                Log.StepStarted(logger, middleware.StepName);
                await middleware.InvokeAsync(context, next, ct);
            };
        }

        return pipeline;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "-> {MiddlewareName}")]
        public static partial void StepStarted(ILogger logger, string middlewareName);

        [LoggerMessage(Level = LogLevel.Trace, Message = "-- {MiddlewareName} (skipped)")]
        public static partial void StepSkipped(ILogger logger, string middlewareName);
    }
}
