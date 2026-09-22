using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Pipeline;

internal interface IIngestionPipelineRunner
{
    Task RunAsync(IngestionContext context, CancellationToken ct);
}

internal sealed partial class IngestionPipelineRunner(
    IEnumerable<IIngestionMiddleware> middlewares,
    IProxyDirectoryStrategy proxyDirectoryStrategy,
    IFileSystem fileSystem,
    IOptions<WorkerSettings> settings,
    ILogger<IngestionPipelineRunner> logger) : IIngestionPipelineRunner
{
    private readonly IngestionDelegate pipeline = BuildValidated([.. middlewares], logger);

    public async Task RunAsync(IngestionContext context, CancellationToken ct)
    {
        try
        {
            await pipeline(context, ct);
        }
        catch (Exception ex) when (!IngestionShutdown.IsInProgress(ex, ct))
        {
            // The pipeline generates proxies before it writes the asset row, so a failure part-way
            // through leaves files on disk that no row will ever reference. This is the one place
            // that sees every file written across every step, so cleanup belongs here.
            Compensate(context);
            throw;
        }
    }

    private void Compensate(IngestionContext context)
    {
        var proxyRoot = settings.Value.ProxyPath;
        var proxies = context.ProxyFiles;

        foreach (var proxy in proxies)
            TryDelete(fileSystem.Path.Combine(proxyRoot, proxy.ProxyPath));

        // Asking the strategy rather than deriving the directory from the proxy paths keeps the
        // path rule in one place, and covers the attempt that failed in its first generator: it
        // registered no proxy at all, but the step had already created the directory.
        if (context.ContentHash is { } contentHash)
        {
            var directory = fileSystem.Path.Combine(
                proxyRoot, proxyDirectoryStrategy.GetDirectory(context.Config.Id, contentHash));
            DiscardTemporaryFiles(directory);
            TryRemoveIfEmpty(directory);
        }

        Log.Compensated(logger, proxies.Count, context.Item.RelativePath);
    }

    // Deletion runs while the original exception is in flight, so a file that cannot be deleted is
    // reported and skipped: one locked file must not strand the others or replace the real error.
    private void TryDelete(string absolutePath)
    {
        try
        {
            if (fileSystem.File.Exists(absolutePath))
                fileSystem.File.Delete(absolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DeleteFailed(logger, absolutePath, ex);
        }
    }

    private void DiscardTemporaryFiles(string directory)
    {
        try
        {
            var pattern = $"*{ProxyStagingWriter.TemporaryFileSuffix}";
            foreach (var temporaryPath in fileSystem.Directory.EnumerateFiles(directory, pattern))
                TryDelete(temporaryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DeleteFailed(logger, directory, ex);
        }
    }

    // Only when empty: the directory is keyed by storage config and content hash, so a duplicate
    // asset can share it until the unique index in #159 lands, and a recursive delete would
    // destroy that sibling's proxies.
    private void TryRemoveIfEmpty(string directory)
    {
        try
        {
            if (fileSystem.Directory.Exists(directory)
                && !fileSystem.Directory.EnumerateFileSystemEntries(directory).Any())
            {
                fileSystem.Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DeleteFailed(logger, directory, ex);
        }
    }

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

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Cleaned up {Count} proxy file(s) after a failed ingestion of {RelativePath}.")]
        public static partial void Compensated(ILogger logger, int count, string relativePath);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete {Path} during cleanup.")]
        public static partial void DeleteFailed(ILogger logger, string path, Exception ex);
    }
}
