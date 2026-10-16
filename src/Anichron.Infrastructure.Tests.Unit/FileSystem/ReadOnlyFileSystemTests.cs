using Anichron.Infrastructure.FileSystem;
using Microsoft.Win32.SafeHandles;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace Anichron.Infrastructure.Tests.Unit.FileSystem;

public sealed class ReadOnlyFileSystemTests
{
    private const string Source = "/data/originals/a.jpg";
    private const string Destination = "/data/originals/b.jpg";

    private static readonly DateTime When = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SafeFileHandle Handle = new(IntPtr.Zero, ownsHandle: false);
    private static readonly string[] Lines = ["x"];
    private static readonly byte[] NoBytes = [];
    private static readonly ReadOnlyMemory<byte>[] NoWriteBuffers = [];
    private static readonly Memory<byte>[] NoReadBuffers = [];

    private static readonly Dictionary<string, Action<IFileSystem>> WriteCalls = new()
    {
        { "File.AppendAllBytes(byte[])", fs => fs.File.AppendAllBytes(Source, [1]) },
        { "File.AppendAllBytes(span)", fs => fs.File.AppendAllBytes(Source, []) },
        { "File.AppendAllBytesAsync(byte[])", fs => _ = fs.File.AppendAllBytesAsync(Source, [1]) },
        { "File.AppendAllBytesAsync(memory)", fs => _ = fs.File.AppendAllBytesAsync(Source, ReadOnlyMemory<byte>.Empty) },
        { "File.AppendAllLines", fs => fs.File.AppendAllLines(Source, Lines) },
        { "File.AppendAllLines(encoding)", fs => fs.File.AppendAllLines(Source, Lines, Encoding.UTF8) },
        { "File.AppendAllLinesAsync", fs => _ = fs.File.AppendAllLinesAsync(Source, Lines) },
        { "File.AppendAllLinesAsync(encoding)", fs => _ = fs.File.AppendAllLinesAsync(Source, Lines, Encoding.UTF8) },
        { "File.AppendAllText", fs => fs.File.AppendAllText(Source, "x") },
        { "File.AppendAllText(span)", fs => fs.File.AppendAllText(Source, "x".AsSpan()) },
        { "File.AppendAllText(encoding)", fs => fs.File.AppendAllText(Source, "x", Encoding.UTF8) },
        { "File.AppendAllText(span, encoding)", fs => fs.File.AppendAllText(Source, "x".AsSpan(), Encoding.UTF8) },
        { "File.AppendAllTextAsync", fs => _ = fs.File.AppendAllTextAsync(Source, "x") },
        { "File.AppendAllTextAsync(memory)", fs => _ = fs.File.AppendAllTextAsync(Source, "x".AsMemory()) },
        { "File.AppendAllTextAsync(encoding)", fs => _ = fs.File.AppendAllTextAsync(Source, "x", Encoding.UTF8) },
        { "File.AppendAllTextAsync(memory, encoding)", fs => _ = fs.File.AppendAllTextAsync(Source, "x".AsMemory(), Encoding.UTF8) },
        { "File.AppendText", fs => fs.File.AppendText(Source) },
        { "File.Copy", fs => fs.File.Copy(Source, Destination) },
        { "File.Copy(overwrite)", fs => fs.File.Copy(Source, Destination, overwrite: true) },
        { "File.Create", fs => fs.File.Create(Source) },
        { "File.Create(bufferSize)", fs => fs.File.Create(Source, 4096) },
        { "File.Create(options)", fs => fs.File.Create(Source, 4096, FileOptions.None) },
        { "File.CreateSymbolicLink", fs => fs.File.CreateSymbolicLink(Source, Destination) },
        { "File.CreateText", fs => fs.File.CreateText(Source) },
        { "File.Delete", fs => fs.File.Delete(Source) },
        { "File.Move", fs => fs.File.Move(Source, Destination) },
        { "File.Move(overwrite)", fs => fs.File.Move(Source, Destination, overwrite: true) },
        { "File.Open(mode)", fs => fs.File.Open(Source, FileMode.Open) },
        { "File.Open(write access)", fs => fs.File.Open(Source, FileMode.Open, FileAccess.Write) },
        { "File.Open(write access, share)", fs => fs.File.Open(Source, FileMode.Open, FileAccess.Write, FileShare.Read) },
        { "File.Open(write options)", fs => fs.File.Open(Source, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Write }) },
        { "File.OpenWrite", fs => fs.File.OpenWrite(Source) },
        { "File.Replace", fs => fs.File.Replace(Source, Destination, null) },
        { "File.Replace(ignoreMetadataErrors)", fs => fs.File.Replace(Source, Destination, null, ignoreMetadataErrors: true) },
        { "File.SetAttributes", fs => fs.File.SetAttributes(Source, FileAttributes.Normal) },
        { "File.SetAttributes(handle)", fs => fs.File.SetAttributes(Handle, FileAttributes.Normal) },
        { "File.SetCreationTime", fs => fs.File.SetCreationTime(Source, When) },
        { "File.SetCreationTime(handle)", fs => fs.File.SetCreationTime(Handle, When) },
        { "File.SetCreationTimeUtc", fs => fs.File.SetCreationTimeUtc(Source, When) },
        { "File.SetCreationTimeUtc(handle)", fs => fs.File.SetCreationTimeUtc(Handle, When) },
        { "File.SetLastAccessTime", fs => fs.File.SetLastAccessTime(Source, When) },
        { "File.SetLastAccessTime(handle)", fs => fs.File.SetLastAccessTime(Handle, When) },
        { "File.SetLastAccessTimeUtc", fs => fs.File.SetLastAccessTimeUtc(Source, When) },
        { "File.SetLastAccessTimeUtc(handle)", fs => fs.File.SetLastAccessTimeUtc(Handle, When) },
        { "File.SetLastWriteTime", fs => fs.File.SetLastWriteTime(Source, When) },
        { "File.SetLastWriteTime(handle)", fs => fs.File.SetLastWriteTime(Handle, When) },
        { "File.SetLastWriteTimeUtc", fs => fs.File.SetLastWriteTimeUtc(Source, When) },
        { "File.SetLastWriteTimeUtc(handle)", fs => fs.File.SetLastWriteTimeUtc(Handle, When) },
        { "File.WriteAllBytes", fs => fs.File.WriteAllBytes(Source, [1]) },
        { "File.WriteAllBytes(span)", fs => fs.File.WriteAllBytes(Source, []) },
        { "File.WriteAllBytesAsync", fs => _ = fs.File.WriteAllBytesAsync(Source, [1]) },
        { "File.WriteAllBytesAsync(memory)", fs => _ = fs.File.WriteAllBytesAsync(Source, ReadOnlyMemory<byte>.Empty) },
        { "File.WriteAllLines(enumerable)", fs => fs.File.WriteAllLines(Source, Lines.AsEnumerable()) },
        { "File.WriteAllLines(array)", fs => fs.File.WriteAllLines(Source, Lines) },
        { "File.WriteAllLines(enumerable, encoding)", fs => fs.File.WriteAllLines(Source, Lines.AsEnumerable(), Encoding.UTF8) },
        { "File.WriteAllLines(array, encoding)", fs => fs.File.WriteAllLines(Source, Lines, Encoding.UTF8) },
        { "File.WriteAllLinesAsync", fs => _ = fs.File.WriteAllLinesAsync(Source, Lines) },
        { "File.WriteAllLinesAsync(encoding)", fs => _ = fs.File.WriteAllLinesAsync(Source, Lines, Encoding.UTF8) },
        { "File.WriteAllText", fs => fs.File.WriteAllText(Source, "x") },
        { "File.WriteAllText(span)", fs => fs.File.WriteAllText(Source, "x".AsSpan()) },
        { "File.WriteAllText(encoding)", fs => fs.File.WriteAllText(Source, "x", Encoding.UTF8) },
        { "File.WriteAllText(span, encoding)", fs => fs.File.WriteAllText(Source, "x".AsSpan(), Encoding.UTF8) },
        { "File.WriteAllTextAsync", fs => _ = fs.File.WriteAllTextAsync(Source, "x") },
        { "File.WriteAllTextAsync(memory)", fs => _ = fs.File.WriteAllTextAsync(Source, "x".AsMemory()) },
        { "File.WriteAllTextAsync(encoding)", fs => _ = fs.File.WriteAllTextAsync(Source, "x", Encoding.UTF8) },
        { "File.WriteAllTextAsync(memory, encoding)", fs => _ = fs.File.WriteAllTextAsync(Source, "x".AsMemory(), Encoding.UTF8) },
        { "Directory.CreateDirectory", fs => fs.Directory.CreateDirectory(Source) },
        { "Directory.CreateDirectory(mode)", fs => fs.Directory.CreateDirectory(Source, UnixFileMode.UserRead) },
        { "Directory.CreateSymbolicLink", fs => fs.Directory.CreateSymbolicLink(Source, Destination) },
        { "Directory.CreateTempSubdirectory", fs => fs.Directory.CreateTempSubdirectory() },
        { "Directory.Delete", fs => fs.Directory.Delete(Source) },
        { "Directory.Delete(recursive)", fs => fs.Directory.Delete(Source, recursive: true) },
        { "Directory.Move", fs => fs.Directory.Move(Source, Destination) },
        { "Directory.SetCreationTime", fs => fs.Directory.SetCreationTime(Source, When) },
        { "Directory.SetCreationTimeUtc", fs => fs.Directory.SetCreationTimeUtc(Source, When) },
        { "Directory.SetLastAccessTime", fs => fs.Directory.SetLastAccessTime(Source, When) },
        { "Directory.SetLastAccessTimeUtc", fs => fs.Directory.SetLastAccessTimeUtc(Source, When) },
        { "Directory.SetLastWriteTime", fs => fs.Directory.SetLastWriteTime(Source, When) },
        { "Directory.SetLastWriteTimeUtc", fs => fs.Directory.SetLastWriteTimeUtc(Source, When) },
        { "FileStream.New(mode)", fs => fs.FileStream.New(Source, FileMode.Open) },
        { "FileStream.New(write access)", fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Write) },
        { "FileStream.New(read access, creating mode)", fs => fs.FileStream.New(Source, FileMode.OpenOrCreate, FileAccess.Read) },
        { "FileStream.New(write options)", fs => fs.FileStream.New(Source, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.ReadWrite }) },
        { "FileStream.New(read options, creating mode)", fs => fs.FileStream.New(Source, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Read }) },
        { "FileStream.New(share)", fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Write, FileShare.Read) },
        { "FileStream.New(bufferSize)", fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Write, FileShare.Read, 4096) },
        { "FileStream.New(useAsync)", fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, useAsync: true) },
        { "FileStream.New(options)", fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Write, FileShare.Read, 4096, FileOptions.None) },
        { "FileStream.New(delete-on-close options)", fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose) },
        { "FileStream.New(delete-on-close stream options)", fs => fs.FileStream.New(Source, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Options = FileOptions.DeleteOnClose }) },
        { "File.Open(delete-on-close stream options)", fs => fs.File.Open(Source, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Options = FileOptions.DeleteOnClose }) },
        { "FileStream.New(handle)", fs => fs.FileStream.New(Handle, FileAccess.Write) },
        { "File.OpenHandle(write access)", fs => fs.File.OpenHandle(Source, FileMode.Open, FileAccess.Write) },
        { "File.OpenHandle(creating mode)", fs => fs.File.OpenHandle(Source, FileMode.OpenOrCreate) },
        { "File.OpenHandle(delete-on-close)", fs => fs.File.OpenHandle(Source, options: FileOptions.DeleteOnClose) },
        { "RandomAccess.Write(span)", fs => fs.RandomAccess.Write(Handle, NoBytes.AsSpan(), 0) },
        { "RandomAccess.Write(buffers)", fs => fs.RandomAccess.Write(Handle, NoWriteBuffers, 0) },
        { "RandomAccess.WriteAsync(memory)", fs => _ = fs.RandomAccess.WriteAsync(Handle, NoBytes, 0).AsTask() },
        { "RandomAccess.WriteAsync(buffers)", fs => _ = fs.RandomAccess.WriteAsync(Handle, NoWriteBuffers, 0).AsTask() },
        { "RandomAccess.SetLength", fs => fs.RandomAccess.SetLength(Handle, 0) },
        { "RandomAccess.FlushToDisk", fs => fs.RandomAccess.FlushToDisk(Handle) },
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
            (fs => fs.File.Open(Source, FileMode.Open, FileAccess.Read),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.None))
        },
        {
            "File.Open(read access, share)",
            (fs => fs.File.Open(Source, FileMode.Open, FileAccess.Read, FileShare.Read),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read))
        },
        {
            "File.Open(read options)",
            (fs => fs.File.Open(Source, ReadOptions),
                fs => fs.FileStream.New(Source, ReadOptions))
        },
        {
            "FileStream.New(read access)",
            (fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read))
        },
        {
            "FileStream.New(read options)",
            (fs => fs.FileStream.New(Source, ReadOptions),
                fs => fs.FileStream.New(Source, ReadOptions))
        },
        {
            "FileStream.New(share)",
            (fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read))
        },
        {
            "FileStream.New(bufferSize)",
            (fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
        },
        {
            "FileStream.New(useAsync)",
            (fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        },
        {
            "FileStream.New(options)",
            (fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous),
                fs => fs.FileStream.New(Source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
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
        using var stream = Substitute.For<FileSystemStream>(Stream.Null, Source, false);
        expectOnInner(inner).Returns(stream);
        var testee = new ReadOnlyFileSystem(inner);

        open(testee).Should().BeSameAs(stream, member);
    }

    private static readonly Dictionary<string, Action<IFileSystem>> UnimplementedReadCalls = new()
    {
        { "File.Exists", fs => fs.File.Exists(Source) },
        { "File.OpenRead", fs => fs.File.OpenRead(Source) },
        { "File.ReadAllBytes", fs => fs.File.ReadAllBytes(Source) },
        { "Directory.EnumerateFiles", fs => fs.Directory.EnumerateFiles(Source) },
        { "Directory.GetFiles", fs => fs.Directory.GetFiles(Source) },
        { "FileStream.Wrap", fs => fs.FileStream.Wrap(null!) },
        { "FileInfo.New", fs => fs.FileInfo.New(Source) },
        { "FileInfo.Wrap", fs => fs.FileInfo.Wrap(null) },
        { "DirectoryInfo.New", fs => fs.DirectoryInfo.New(Source) },
        { "DirectoryInfo.Wrap", fs => fs.DirectoryInfo.Wrap(null) },
        { "RandomAccess.GetLength", fs => fs.RandomAccess.GetLength(Handle) },
        { "RandomAccess.Read(span)", fs => fs.RandomAccess.Read(Handle, NoBytes.AsSpan(), 0) },
        { "RandomAccess.Read(buffers)", fs => fs.RandomAccess.Read(Handle, NoReadBuffers, 0) },
        { "RandomAccess.ReadAsync(memory)", fs => _ = fs.RandomAccess.ReadAsync(Handle, NoBytes, 0).AsTask() },
        { "RandomAccess.ReadAsync(buffers)", fs => _ = fs.RandomAccess.ReadAsync(Handle, NoReadBuffers, 0).AsTask() },
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
        testee.RandomAccess.FileSystem.Should().BeSameAs(testee);
    }

    [Fact]
    public void ReadOnlyHandle_ForwardsToTheInnerFileSystem()
    {
        var inner = Substitute.For<IFileSystem>();
        inner.File.OpenHandle(Source, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None, 0).Returns(Handle);
        var testee = new ReadOnlyFileSystem(inner);

        testee.File.OpenHandle(Source).Should().BeSameAs(Handle);
    }
}
