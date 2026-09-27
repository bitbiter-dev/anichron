using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.TestDoubles;

// Makes one path's deletion fail while every other operation keeps the in-memory filesystem's
// behaviour — MockFileSystem cannot express a delete failure. Derives from MockFileSystem rather
// than delegating to IFileSystem because IFile is far too wide to forward member by member.
internal sealed class DeleteFailingFileSystem : MockFileSystem
{
    public DeleteFailingFileSystem(string pathThatFailsToDelete)
        => File = new DeleteFailingFile(this, pathThatFailsToDelete);

    public override IFile File { get; }

    private sealed class DeleteFailingFile(MockFileSystem fileSystem, string pathThatFailsToDelete)
        : MockFile(fileSystem)
    {
        public override void Delete(string path)
        {
            if (path == pathThatFailsToDelete)
                throw new IOException($"Simulated delete failure for {path}.");

            base.Delete(path);
        }
    }
}
