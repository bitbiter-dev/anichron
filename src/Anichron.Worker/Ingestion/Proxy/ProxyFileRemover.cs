using Anichron.Core;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Proxy;

/// <summary>Deletes a proxy file, then its content folder once that is empty, never a shard folder.</summary>
/// <param name="directories">Names a file's content folder.</param>
/// <param name="fileSystem">Holds the proxy root.</param>
/// <param name="logger">Logs what could not be removed.</param>
internal sealed partial class ProxyFileRemover(
    IProxyDirectoryStrategy directories,
    IFileSystem fileSystem,
    ILogger<ProxyFileRemover> logger)
{
    /// <summary>Deletes the file unless it is already gone, then its emptied content folder.</summary>
    /// <param name="proxyPath">The file's path relative to the proxy root.</param>
    /// <param name="file">The same file, resolved under the proxy root.</param>
    /// <returns>False, logged, when the file could not be deleted.</returns>
    public bool Delete(string proxyPath, string file)
    {
        try
        {
            if (fileSystem.File.Exists(file))
                fileSystem.File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DeleteFailed(logger, proxyPath, ex);
            return false;
        }

        RemoveContentDirectoryIfEmpty(proxyPath);
        return true;
    }

    private void RemoveContentDirectoryIfEmpty(string proxyPath)
    {
        if (directories.ContentDirectoryOf(proxyPath) is not { } content)
            return;
        if (StoragePath.ResolveProxy(content) is not { } directory)
        {
            Log.DirectoryOutsideRoot(logger, content);
            return;
        }

        try
        {
            if (!fileSystem.Directory.EnumerateFileSystemEntries(directory).Any())
                fileSystem.Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.DirectoryKept(logger, directory, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete proxy file {ProxyPath}.")]
        public static partial void DeleteFailed(ILogger logger, string proxyPath, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Kept proxy directory {Directory}: it resolves outside the proxy root.")]
        public static partial void DirectoryOutsideRoot(ILogger logger, string directory);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Kept proxy directory {Directory}: it could not be removed.")]
        public static partial void DirectoryKept(ILogger logger, string directory, Exception ex);
    }
}
