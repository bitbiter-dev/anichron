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
            // Capture loop variables into locals; lambdas close over the local, not
            // the loop variable, so each iteration gets its own independent copy.
            var next = pipeline;
            var current = middleware;
            pipeline = async (context, ct) =>
            {
                if (!current.CanInvoke(context))
                {
                    var error = current.OnCannotInvoke(context);
                    throw new PipelineConfigurationException(
                        $"{current.StepName} cannot invoke: {error.Message}");
                }

                Log.StepStarted(logger, current.StepName);
                await current.InvokeAsync(context, next, ct);
            };
        }

        return pipeline;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "-> {MiddlewareName}")]
        public static partial void StepStarted(ILogger logger, string middlewareName);
    }
}
