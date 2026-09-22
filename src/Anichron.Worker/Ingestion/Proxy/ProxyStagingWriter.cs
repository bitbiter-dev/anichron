using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Proxy;

internal sealed partial class ProxyStagingWriter(
    IFileSystem fileSystem,
    IOptions<WorkerSettings> settings,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<ProxyStagingWriter> logger)
{
    // A proxy is written under this suffix and renamed into place once complete, so a crash or a
    // killed transcode can never leave a truncated file wearing a valid proxy's name. Leftovers
    // are recognisable by this suffix, both to an operator and to the orphan sweeper (#174).
    internal const string TemporaryFileSuffix = ".tmp";

    internal static string TemporaryPathFor(string proxyAbsolutePath)
        => proxyAbsolutePath + TemporaryFileSuffix;

    // The one place a proxy reaches its final name: produceAsync writes the bytes to the temporary
    // path it is handed, and only a complete file is renamed into place and registered on the
    // context. The rename stays within one directory, so it is atomic.
    internal async Task WriteAsync(
        IngestionContext context,
        string relativePath,
        ProxyType proxyType,
        Func<string, CancellationToken, Task> produceAsync,
        CancellationToken ct)
    {
        var proxyAbsolutePath = fileSystem.Path.Combine(settings.Value.ProxyPath, relativePath);
        var temporaryAbsolutePath = TemporaryPathFor(proxyAbsolutePath);
        try
        {
            if (fileSystem.Path.GetDirectoryName(proxyAbsolutePath) is { } directory)
                fileSystem.Directory.CreateDirectory(directory);

            await produceAsync(temporaryAbsolutePath, ct);
            var sizeBytes = fileSystem.FileInfo.New(temporaryAbsolutePath).Length;
            fileSystem.File.Move(temporaryAbsolutePath, proxyAbsolutePath, overwrite: true);

            // Registered at the moment it lands, so the context always reflects what is on disk
            // and compensation can delete precisely the files this attempt wrote.
            context.AddProxyFile(
                ProxyFileBuilder.Build(context, relativePath, proxyType, sizeBytes, guidFactory, clock));
            Log.ProxyWritten(logger, proxyType, sizeBytes, context.Item.RelativePath);
        }
        finally
        {
            DiscardTemporaryFile(temporaryAbsolutePath);
        }
    }

    // Runs while an exception may already be in flight — including a shutdown cancellation, where
    // the half-written file is dropped but completed proxies are kept. A failure to delete must
    // never replace the error that caused the cleanup.
    private void DiscardTemporaryFile(string temporaryPath)
    {
        try
        {
            if (fileSystem.File.Exists(temporaryPath))
                fileSystem.File.Delete(temporaryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DiscardFailed(logger, temporaryPath, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote {ProxyType} proxy ({SizeBytes} B) for {RelativePath}.")]
        public static partial void ProxyWritten(ILogger logger, ProxyType proxyType, long sizeBytes, string relativePath);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete temporary proxy file {Path}.")]
        public static partial void DiscardFailed(ILogger logger, string path, Exception ex);
    }
}
