using Anichron.API.Settings;
using Anichron.Core.Domain;
using System.IO.Abstractions;

namespace Anichron.API.Services;

public interface IBlurhashReader
{
    Task<string?> ReadAsync(MediaAsset asset, CancellationToken ct);
}

public sealed partial class BlurhashReader(IFileSystem fileSystem, ILogger<BlurhashReader> logger) : IBlurhashReader
{
    public async Task<string?> ReadAsync(MediaAsset asset, CancellationToken ct)
    {
        var proxy = asset.ProxyFiles.FirstOrDefault(p => p.ProxyType == ProxyType.BlurHash);
        if (proxy is null)
            return null;

        try
        {
            return await fileSystem.File.ReadAllTextAsync(
                fileSystem.Path.Join(AppDefaults.Storage.ProxyPath, proxy.ProxyPath), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Unreadable(logger, proxy.ProxyPath, asset.Id, ex);
            return null;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Blurhash proxy {ProxyPath} of asset {AssetId} could not be read; serving the asset without a placeholder.")]
        public static partial void Unreadable(ILogger logger, string proxyPath, Guid assetId, Exception exception);
    }
}
