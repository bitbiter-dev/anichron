using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.TestDoubles;

internal sealed class EnumerationFailingFileSystem : MockFileSystem
{
    private readonly EnumerationFailingDirectory? failingDirectory;

    public EnumerationFailingFileSystem(Func<Exception> failure)
        => failingDirectory = new EnumerationFailingDirectory(this, failure);

    public override IDirectory Directory => failingDirectory ?? base.Directory;

    private sealed class EnumerationFailingDirectory(MockFileSystem fileSystem, Func<Exception> failure)
        : MockDirectory(fileSystem, fileSystem.Directory.GetCurrentDirectory())
    {
        public override IEnumerable<string> EnumerateFiles(string path, string searchPattern)
            => throw failure();

        public override IEnumerable<string> EnumerateFileSystemEntries(string path)
            => throw failure();
    }
}
