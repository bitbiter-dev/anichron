namespace Anichron.Worker.Ingestion.Pipeline;

internal static class IngestionPipelineBuilder
{
    internal static IngestionDelegate Build(IReadOnlyList<IIngestionMiddleware> middlewares)
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
                        $"{current.GetType().Name} cannot invoke: {error.Message}");
                }

                await current.InvokeAsync(context, next, ct);
            };
        }

        return pipeline;
    }
}
