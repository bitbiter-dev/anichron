using Anichron.Core;
using System.IO.Abstractions;

namespace Anichron.Infrastructure.Storage;

public interface IOriginalStore
{
    bool RootExists();
}

public sealed class OriginalStore : IOriginalStore
{
    private readonly IFileSystem fileSystem;

    public OriginalStore()
        : this(new FileSystem())
    {
    }

    internal OriginalStore(IFileSystem fileSystem) => this.fileSystem = fileSystem;

    public bool RootExists() => fileSystem.Directory.Exists(StoragePath.OriginalsRoot);
}
