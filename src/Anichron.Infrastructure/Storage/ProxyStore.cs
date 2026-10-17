using Anichron.Core;
using System.IO.Abstractions;

namespace Anichron.Infrastructure.Storage;

public interface IProxyStore
{
    bool RootExists();

    /// <summary>
    /// Opens a proxy for reading by its stored path, relative to the proxy root. Returns
    /// <see langword="null"/> when the file is missing or the path resolves outside the root.
    /// </summary>
    Stream? OpenRead(string proxyPath);
}

public sealed class ProxyStore : IProxyStore
{
    private readonly IFileSystem fileSystem;

    public ProxyStore()
        : this(new FileSystem())
    {
    }

    internal ProxyStore(IFileSystem fileSystem) => this.fileSystem = fileSystem;

    public bool RootExists() => fileSystem.Directory.Exists(StoragePath.ProxiesRoot);

    public Stream? OpenRead(string proxyPath)
    {
        var fullPath = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(StoragePath.ProxiesRoot, proxyPath));
        if (!StoragePath.IsInside(StoragePath.ProxiesRoot, fullPath))
            return null;

        try
        {
            return fileSystem.FileStream.New(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
