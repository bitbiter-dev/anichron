using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.TestDoubles;

public sealed class DeleteFailingFileSystemTests
{
    [Fact]
    public void Delete_ConfiguredPath_ThrowsIOException()
    {
        var fileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/preview.jpg");
        fileSystem.AddFile("/proxies/ab/cd/preview.jpg", new MockFileData([0x01]));

        var act = () => fileSystem.File.Delete("/proxies/ab/cd/preview.jpg");

        act.Should().Throw<IOException>();
    }

    [Fact]
    public void Delete_ConfiguredPath_LeavesTheFileOnDisk()
    {
        var fileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/preview.jpg");
        fileSystem.AddFile("/proxies/ab/cd/preview.jpg", new MockFileData([0x01]));

        var act = () => fileSystem.File.Delete("/proxies/ab/cd/preview.jpg");

        act.Should().Throw<IOException>();
        fileSystem.FileExists("/proxies/ab/cd/preview.jpg").Should().BeTrue();
    }

    [Fact]
    public void Delete_OtherPath_DeletesTheFile()
    {
        var fileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/preview.jpg");
        fileSystem.AddFile("/proxies/ab/cd/thumbnail.jpg", new MockFileData([0x01]));

        fileSystem.File.Delete("/proxies/ab/cd/thumbnail.jpg");

        fileSystem.FileExists("/proxies/ab/cd/thumbnail.jpg").Should().BeFalse();
    }

    [Fact]
    public void WriteAllText_AnyPath_DelegatesToTheInMemoryFilesystem()
    {
        var fileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/preview.jpg");
        fileSystem.Directory.CreateDirectory("/proxies/ab/cd");

        fileSystem.File.WriteAllText("/proxies/ab/cd/preview.jpg", "staged");

        fileSystem.File.ReadAllText("/proxies/ab/cd/preview.jpg").Should().Be("staged");
    }

    [Fact]
    public void Exists_ConfiguredPath_DelegatesToTheInMemoryFilesystem()
    {
        var fileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/preview.jpg");
        fileSystem.AddFile("/proxies/ab/cd/preview.jpg", new MockFileData([0x01]));

        fileSystem.File.Exists("/proxies/ab/cd/preview.jpg").Should().BeTrue();
    }

    [Fact]
    public void DirectoryDelete_AnyPath_DelegatesToTheInMemoryFilesystem()
    {
        var fileSystem = new DeleteFailingFileSystem("/proxies/ab/cd/preview.jpg");
        fileSystem.Directory.CreateDirectory("/proxies/ab/cd");

        fileSystem.Directory.Delete("/proxies/ab/cd");

        fileSystem.Directory.Exists("/proxies/ab/cd").Should().BeFalse();
    }
}
