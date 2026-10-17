using Anichron.Core;
using Anichron.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Infrastructure.Tests.Unit.Storage;

public sealed class ProxyStoreTests
{
    private static MockFileSystem FileSystemWith(params string[] files)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(),
            new MockFileSystemOptions { CurrentDirectory = "/" });
        foreach (var file in files)
            fileSystem.AddFile(file, new MockFileData([9, 9, 9]));
        return fileSystem;
    }

    [Fact]
    public void RootExists_ProxyRootPresent_ReturnsTrue()
    {
        var fileSystem = FileSystemWith();
        fileSystem.Directory.CreateDirectory(StoragePath.ProxiesRoot);

        new ProxyStore(fileSystem).RootExists().Should().BeTrue();
    }

    [Fact]
    public void RootExists_OnlyASiblingPresent_ReturnsFalse()
    {
        var fileSystem = FileSystemWith();
        fileSystem.Directory.CreateDirectory("/data/proxies2");

        new ProxyStore(fileSystem).RootExists().Should().BeFalse();
    }

    [Fact]
    public void OpenRead_FileInsideTheRoot_StreamsItsBytes()
    {
        var fileSystem = FileSystemWith();
        fileSystem.AddFile("/data/proxies/01/thumbnail.jpg", new MockFileData([1, 2, 3]));

        using var stream = new ProxyStore(fileSystem).OpenRead("01/thumbnail.jpg");

        stream.Should().NotBeNull();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void OpenRead_FileInsideTheRoot_OpensItReadOnly()
    {
        var fileSystem = FileSystemWith("/data/proxies/01/thumbnail.jpg");

        using var stream = new ProxyStore(fileSystem).OpenRead("01/thumbnail.jpg");

        stream!.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void OpenRead_FileInsideTheRoot_LetsOtherReadersShareIt()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Path.Returns(new MockFileSystem().Path);

        _ = new ProxyStore(fileSystem).OpenRead("01/thumbnail.jpg");

        fileSystem.FileStream.Received(1).New(
            "/data/proxies/01/thumbnail.jpg", FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    [Fact]
    public void OpenRead_MissingFile_ReturnsNull()
    {
        var fileSystem = FileSystemWith();
        fileSystem.Directory.CreateDirectory("/data/proxies/01");

        new ProxyStore(fileSystem).OpenRead("01/thumbnail.jpg").Should().BeNull();
    }

    [Fact]
    public void OpenRead_MissingDirectory_ReturnsNull()
    {
        new ProxyStore(FileSystemWith()).OpenRead("01/thumbnail.jpg").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../originals/mira/private.jpg")]
    [InlineData("/data/originals/mira/private.jpg")]
    [InlineData("01/../../proxies2/x.jpg")]
    [InlineData("../proxies2/x.jpg")]
    public void OpenRead_PathEscapingTheRoot_ReturnsNullEvenWhenTheFileExists(string proxyPath)
    {
        var fileSystem = FileSystemWith("/data/originals/mira/private.jpg", "/data/proxies2/x.jpg");

        new ProxyStore(fileSystem).OpenRead(proxyPath).Should().BeNull();
    }

    [Fact]
    public void OpenRead_UnreadableFile_Throws()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Path.Returns(new MockFileSystem().Path);
        fileSystem.FileStream.New(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
            .Returns(_ => throw new UnauthorizedAccessException());

        var act = () => new ProxyStore(fileSystem).OpenRead("01/thumbnail.jpg");

        act.Should().Throw<UnauthorizedAccessException>();
    }
}
