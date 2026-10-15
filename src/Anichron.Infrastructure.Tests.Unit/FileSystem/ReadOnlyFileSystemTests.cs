using Anichron.Infrastructure.FileSystem;
using Microsoft.Win32.SafeHandles;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace Anichron.Infrastructure.Tests.Unit.FileSystem;

public sealed class ReadOnlyFileSystemTests
{
    private const string P = "/data/originals/a.jpg";
    private const string Q = "/data/originals/b.jpg";

    private static readonly DateTime When = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SafeFileHandle Handle = new(IntPtr.Zero, ownsHandle: false);
    private static readonly string[] Lines = ["x"];

    private static readonly Dictionary<string, Action<IFileSystem>> WriteCalls = new()
    {
        { "File.AppendAllBytes(byte[])", fs => fs.File.AppendAllBytes(P, [1]) },
        { "File.AppendAllBytes(span)", fs => fs.File.AppendAllBytes(P, []) },
        { "File.AppendAllBytesAsync(byte[])", fs => _ = fs.File.AppendAllBytesAsync(P, [1]) },
        { "File.AppendAllBytesAsync(memory)", fs => _ = fs.File.AppendAllBytesAsync(P, ReadOnlyMemory<byte>.Empty) },
        { "File.AppendAllLines", fs => fs.File.AppendAllLines(P, Lines) },
        { "File.AppendAllLines(encoding)", fs => fs.File.AppendAllLines(P, Lines, Encoding.UTF8) },
        { "File.AppendAllLinesAsync", fs => _ = fs.File.AppendAllLinesAsync(P, Lines) },
        { "File.AppendAllLinesAsync(encoding)", fs => _ = fs.File.AppendAllLinesAsync(P, Lines, Encoding.UTF8) },
        { "File.AppendAllText", fs => fs.File.AppendAllText(P, "x") },
        { "File.AppendAllText(span)", fs => fs.File.AppendAllText(P, "x".AsSpan()) },
        { "File.AppendAllText(encoding)", fs => fs.File.AppendAllText(P, "x", Encoding.UTF8) },
        { "File.AppendAllText(span, encoding)", fs => fs.File.AppendAllText(P, "x".AsSpan(), Encoding.UTF8) },
        { "File.AppendAllTextAsync", fs => _ = fs.File.AppendAllTextAsync(P, "x") },
        { "File.AppendAllTextAsync(memory)", fs => _ = fs.File.AppendAllTextAsync(P, "x".AsMemory()) },
        { "File.AppendAllTextAsync(encoding)", fs => _ = fs.File.AppendAllTextAsync(P, "x", Encoding.UTF8) },
        { "File.AppendAllTextAsync(memory, encoding)", fs => _ = fs.File.AppendAllTextAsync(P, "x".AsMemory(), Encoding.UTF8) },
        { "File.AppendText", fs => fs.File.AppendText(P) },
        { "File.Copy", fs => fs.File.Copy(P, Q) },
        { "File.Copy(overwrite)", fs => fs.File.Copy(P, Q, overwrite: true) },
        { "File.Create", fs => fs.File.Create(P) },
        { "File.Create(bufferSize)", fs => fs.File.Create(P, 4096) },
        { "File.Create(options)", fs => fs.File.Create(P, 4096, FileOptions.None) },
        { "File.CreateSymbolicLink", fs => fs.File.CreateSymbolicLink(P, Q) },
        { "File.CreateText", fs => fs.File.CreateText(P) },
        { "File.Delete", fs => fs.File.Delete(P) },
        { "File.Move", fs => fs.File.Move(P, Q) },
        { "File.Move(overwrite)", fs => fs.File.Move(P, Q, overwrite: true) },
        { "File.Open(mode)", fs => fs.File.Open(P, FileMode.Open) },
        { "File.Open(write access)", fs => fs.File.Open(P, FileMode.Open, FileAccess.Write) },
        { "File.Open(write access, share)", fs => fs.File.Open(P, FileMode.Open, FileAccess.Write, FileShare.Read) },
        { "File.Open(write options)", fs => fs.File.Open(P, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Write }) },
        { "File.OpenWrite", fs => fs.File.OpenWrite(P) },
        { "File.Replace", fs => fs.File.Replace(P, Q, null) },
        { "File.Replace(ignoreMetadataErrors)", fs => fs.File.Replace(P, Q, null, ignoreMetadataErrors: true) },
        { "File.SetAttributes", fs => fs.File.SetAttributes(P, FileAttributes.Normal) },
        { "File.SetAttributes(handle)", fs => fs.File.SetAttributes(Handle, FileAttributes.Normal) },
        { "File.SetCreationTime", fs => fs.File.SetCreationTime(P, When) },
        { "File.SetCreationTime(handle)", fs => fs.File.SetCreationTime(Handle, When) },
        { "File.SetCreationTimeUtc", fs => fs.File.SetCreationTimeUtc(P, When) },
        { "File.SetCreationTimeUtc(handle)", fs => fs.File.SetCreationTimeUtc(Handle, When) },
        { "File.SetLastAccessTime", fs => fs.File.SetLastAccessTime(P, When) },
        { "File.SetLastAccessTime(handle)", fs => fs.File.SetLastAccessTime(Handle, When) },
        { "File.SetLastAccessTimeUtc", fs => fs.File.SetLastAccessTimeUtc(P, When) },
        { "File.SetLastAccessTimeUtc(handle)", fs => fs.File.SetLastAccessTimeUtc(Handle, When) },
        { "File.SetLastWriteTime", fs => fs.File.SetLastWriteTime(P, When) },
        { "File.SetLastWriteTime(handle)", fs => fs.File.SetLastWriteTime(Handle, When) },
        { "File.SetLastWriteTimeUtc", fs => fs.File.SetLastWriteTimeUtc(P, When) },
        { "File.SetLastWriteTimeUtc(handle)", fs => fs.File.SetLastWriteTimeUtc(Handle, When) },
        { "File.WriteAllBytes", fs => fs.File.WriteAllBytes(P, [1]) },
        { "File.WriteAllBytes(span)", fs => fs.File.WriteAllBytes(P, []) },
        { "File.WriteAllBytesAsync", fs => _ = fs.File.WriteAllBytesAsync(P, [1]) },
        { "File.WriteAllBytesAsync(memory)", fs => _ = fs.File.WriteAllBytesAsync(P, ReadOnlyMemory<byte>.Empty) },
        { "File.WriteAllLines(enumerable)", fs => fs.File.WriteAllLines(P, Lines.AsEnumerable()) },
        { "File.WriteAllLines(array)", fs => fs.File.WriteAllLines(P, Lines) },
        { "File.WriteAllLines(enumerable, encoding)", fs => fs.File.WriteAllLines(P, Lines.AsEnumerable(), Encoding.UTF8) },
        { "File.WriteAllLines(array, encoding)", fs => fs.File.WriteAllLines(P, Lines, Encoding.UTF8) },
        { "File.WriteAllLinesAsync", fs => _ = fs.File.WriteAllLinesAsync(P, Lines) },
        { "File.WriteAllLinesAsync(encoding)", fs => _ = fs.File.WriteAllLinesAsync(P, Lines, Encoding.UTF8) },
        { "File.WriteAllText", fs => fs.File.WriteAllText(P, "x") },
        { "File.WriteAllText(span)", fs => fs.File.WriteAllText(P, "x".AsSpan()) },
        { "File.WriteAllText(encoding)", fs => fs.File.WriteAllText(P, "x", Encoding.UTF8) },
        { "File.WriteAllText(span, encoding)", fs => fs.File.WriteAllText(P, "x".AsSpan(), Encoding.UTF8) },
        { "File.WriteAllTextAsync", fs => _ = fs.File.WriteAllTextAsync(P, "x") },
        { "File.WriteAllTextAsync(memory)", fs => _ = fs.File.WriteAllTextAsync(P, "x".AsMemory()) },
        { "File.WriteAllTextAsync(encoding)", fs => _ = fs.File.WriteAllTextAsync(P, "x", Encoding.UTF8) },
        { "File.WriteAllTextAsync(memory, encoding)", fs => _ = fs.File.WriteAllTextAsync(P, "x".AsMemory(), Encoding.UTF8) },
        { "Directory.CreateDirectory", fs => fs.Directory.CreateDirectory(P) },
        { "Directory.CreateDirectory(mode)", fs => fs.Directory.CreateDirectory(P, UnixFileMode.UserRead) },
        { "Directory.CreateSymbolicLink", fs => fs.Directory.CreateSymbolicLink(P, Q) },
        { "Directory.CreateTempSubdirectory", fs => fs.Directory.CreateTempSubdirectory() },
        { "Directory.Delete", fs => fs.Directory.Delete(P) },
        { "Directory.Delete(recursive)", fs => fs.Directory.Delete(P, recursive: true) },
        { "Directory.Move", fs => fs.Directory.Move(P, Q) },
        { "Directory.SetCreationTime", fs => fs.Directory.SetCreationTime(P, When) },
        { "Directory.SetCreationTimeUtc", fs => fs.Directory.SetCreationTimeUtc(P, When) },
        { "Directory.SetLastAccessTime", fs => fs.Directory.SetLastAccessTime(P, When) },
        { "Directory.SetLastAccessTimeUtc", fs => fs.Directory.SetLastAccessTimeUtc(P, When) },
        { "Directory.SetLastWriteTime", fs => fs.Directory.SetLastWriteTime(P, When) },
        { "Directory.SetLastWriteTimeUtc", fs => fs.Directory.SetLastWriteTimeUtc(P, When) },
        { "FileStream.New(mode)", fs => fs.FileStream.New(P, FileMode.Open) },
        { "FileStream.New(write access)", fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Write) },
        { "FileStream.New(read access, creating mode)", fs => fs.FileStream.New(P, FileMode.OpenOrCreate, FileAccess.Read) },
        { "FileStream.New(write options)", fs => fs.FileStream.New(P, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.ReadWrite }) },
        { "FileStream.New(read options, creating mode)", fs => fs.FileStream.New(P, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Read }) },
        { "FileStream.New(share)", fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Write, FileShare.Read) },
        { "FileStream.New(bufferSize)", fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Write, FileShare.Read, 4096) },
        { "FileStream.New(useAsync)", fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, useAsync: true) },
        { "FileStream.New(options)", fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, FileOptions.None) },
        { "FileStream.New(handle)", fs => fs.FileStream.New(Handle, FileAccess.Write) },
        { "FileStream.New(handle, bufferSize)", fs => fs.FileStream.New(Handle, FileAccess.ReadWrite, 4096) },
        { "FileStream.New(handle, isAsync)", fs => fs.FileStream.New(Handle, FileAccess.Write, 4096, isAsync: true) },
    };

    public static TheoryData<string> Writes() => [.. WriteCalls.Keys];

    [Theory]
    [MemberData(nameof(Writes))]
    public void Write_ThrowsNotSupported_WithoutTouchingTheInnerFileSystem(string member)
    {
        var write = WriteCalls[member];
        var inner = Substitute.For<IFileSystem>();
        var testee = new ReadOnlyFileSystem(inner);

        var act = () => write(testee);

        act.Should().Throw<NotSupportedException>(member).WithMessage("This file system is read-only.");
        inner.ReceivedCalls().Should().BeEmpty(member);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectoryExists_ForwardsToTheInnerFileSystem(bool exists)
    {
        var inner = new MockFileSystem();
        if (exists)
        {
            inner.Directory.CreateDirectory("/data/originals");
        }

        var testee = new ReadOnlyFileSystem(inner);

        testee.Directory.Exists("/data/originals").Should().Be(exists);
    }

    private static readonly FileStreamOptions ReadOptions = new() { Mode = FileMode.Open, Access = FileAccess.Read };

    private static readonly Dictionary<string, (Func<IFileSystem, FileSystemStream> Open, Func<IFileSystem, FileSystemStream> ExpectOnInner)> ReadCalls = new()
    {
        {
            "File.Open(read access)",
            (fs => fs.File.Open(P, FileMode.Open, FileAccess.Read),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.None))
        },
        {
            "File.Open(read access, share)",
            (fs => fs.File.Open(P, FileMode.Open, FileAccess.Read, FileShare.Read),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read))
        },
        {
            "File.Open(read options)",
            (fs => fs.File.Open(P, ReadOptions),
                fs => fs.FileStream.New(P, ReadOptions))
        },
        {
            "FileStream.New(read access)",
            (fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read))
        },
        {
            "FileStream.New(read options)",
            (fs => fs.FileStream.New(P, ReadOptions),
                fs => fs.FileStream.New(P, ReadOptions))
        },
        {
            "FileStream.New(share)",
            (fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read))
        },
        {
            "FileStream.New(bufferSize)",
            (fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read, 4096),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
        },
        {
            "FileStream.New(useAsync)",
            (fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        },
        {
            "FileStream.New(options)",
            (fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous),
                fs => fs.FileStream.New(P, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
        },
        {
            "FileStream.New(handle)",
            (fs => fs.FileStream.New(Handle, FileAccess.Read),
                fs => fs.FileStream.New(Handle, FileAccess.Read))
        },
        {
            "FileStream.New(handle, bufferSize)",
            (fs => fs.FileStream.New(Handle, FileAccess.Read, 4096),
                fs => fs.FileStream.New(Handle, FileAccess.Read, 4096))
        },
        {
            "FileStream.New(handle, isAsync)",
            (fs => fs.FileStream.New(Handle, FileAccess.Read, 4096, isAsync: true),
                fs => fs.FileStream.New(Handle, FileAccess.Read, 4096, isAsync: true))
        },
    };

    public static TheoryData<string> Reads() => [.. ReadCalls.Keys];

    [Theory]
    [MemberData(nameof(Reads))]
    public void ReadOnlyStream_ForwardsToTheInnerFileSystem(string member)
    {
        var (open, expectOnInner) = ReadCalls[member];
        var inner = Substitute.For<IFileSystem>();
        using var stream = Substitute.For<FileSystemStream>(Stream.Null, P, false);
        expectOnInner(inner).Returns(stream);
        var testee = new ReadOnlyFileSystem(inner);

        open(testee).Should().BeSameAs(stream, member);
    }

    private static readonly Dictionary<string, Action<IFileSystem>> UnimplementedReadCalls = new()
    {
        { "File.Exists", fs => fs.File.Exists(P) },
        { "File.OpenRead", fs => fs.File.OpenRead(P) },
        { "File.ReadAllBytes", fs => fs.File.ReadAllBytes(P) },
        { "Directory.EnumerateFiles", fs => fs.Directory.EnumerateFiles(P) },
        { "Directory.GetFiles", fs => fs.Directory.GetFiles(P) },
        { "FileStream.Wrap", fs => fs.FileStream.Wrap(null!) },
        { "FileInfo.New", fs => fs.FileInfo.New(P) },
        { "FileInfo.Wrap", fs => fs.FileInfo.Wrap(null) },
        { "DirectoryInfo.New", fs => fs.DirectoryInfo.New(P) },
        { "DirectoryInfo.Wrap", fs => fs.DirectoryInfo.Wrap(null) },
    };

    public static TheoryData<string> UnimplementedReads() => [.. UnimplementedReadCalls.Keys];

    [Theory]
    [MemberData(nameof(UnimplementedReads))]
    public void UnforwardedRead_ThrowsNotImplemented(string member)
    {
        var read = UnimplementedReadCalls[member];
        var testee = new ReadOnlyFileSystem(Substitute.For<IFileSystem>());

        var act = () => read(testee);

        act.Should().Throw<NotImplementedException>(member);
    }

    [Fact]
    public void WriteFreeComponents_AreTheInnerFileSystemsOwn()
    {
        var inner = Substitute.For<IFileSystem>();
        var testee = new ReadOnlyFileSystem(inner);

        testee.Path.Should().BeSameAs(inner.Path);
        testee.DriveInfo.Should().BeSameAs(inner.DriveInfo);
        testee.FileVersionInfo.Should().BeSameAs(inner.FileVersionInfo);
        testee.FileSystemWatcher.Should().BeSameAs(inner.FileSystemWatcher);
    }

    [Fact]
    public void WrappedComponents_ReportTheReadOnlyFileSystemAsTheirOwner()
    {
        var testee = new ReadOnlyFileSystem(Substitute.For<IFileSystem>());

        testee.File.FileSystem.Should().BeSameAs(testee);
        testee.Directory.FileSystem.Should().BeSameAs(testee);
        testee.FileStream.FileSystem.Should().BeSameAs(testee);
        testee.FileInfo.FileSystem.Should().BeSameAs(testee);
        testee.DirectoryInfo.FileSystem.Should().BeSameAs(testee);
    }
}
