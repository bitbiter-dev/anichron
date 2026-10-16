using Microsoft.Win32.SafeHandles;
using System.IO.Abstractions;
using System.Text;
using static Anichron.Infrastructure.FileSystem.ReadOnlyFileSystem;

namespace Anichron.Infrastructure.FileSystem;

public sealed class ReadOnlyFileSystem : FileSystemBase
{
    private readonly IFileSystem inner;

    public ReadOnlyFileSystem(IFileSystem inner)
    {
        this.inner = inner;
        File = new ReadOnlyFile(this, inner);
        Directory = new ReadOnlyDirectory(this, inner);
        FileStream = new ReadOnlyFileStreamFactory(this, inner);
        FileInfo = new UnimplementedFileInfoFactory(this);
        DirectoryInfo = new UnimplementedDirectoryInfoFactory(this);
        RandomAccess = new ReadOnlyRandomAccess(this);
    }

    public override IFile File { get; }

    public override IDirectory Directory { get; }

    public override IFileStreamFactory FileStream { get; }

    public override IFileInfoFactory FileInfo { get; }

    public override IDirectoryInfoFactory DirectoryInfo { get; }

    public override IRandomAccess RandomAccess { get; }

    public override IPath Path => inner.Path;

    public override IDriveInfoFactory DriveInfo => inner.DriveInfo;

    public override IFileVersionInfoFactory FileVersionInfo => inner.FileVersionInfo;

    public override IFileSystemWatcherFactory FileSystemWatcher => inner.FileSystemWatcher;

    internal static NotSupportedException ReadOnlyViolation() => new("This file system is read-only.");

    internal static void EnsureReadOnly(FileMode mode, FileAccess access)
    {
        if (mode != FileMode.Open || (access & FileAccess.Write) != 0)
        {
            throw ReadOnlyViolation();
        }
    }

    internal static void EnsureReadOnly(FileMode mode, FileAccess access, FileOptions options)
    {
        if ((options & FileOptions.DeleteOnClose) != 0)
        {
            throw ReadOnlyViolation();
        }

        EnsureReadOnly(mode, access);
    }

    internal static void EnsureReadOnly(FileAccess access)
    {
        if ((access & FileAccess.Write) != 0)
        {
            throw ReadOnlyViolation();
        }
    }
}

internal sealed class ReadOnlyFile(ReadOnlyFileSystem fileSystem, IFileSystem inner) : FileBase(fileSystem)
{
    public override void AppendAllBytes(string path, byte[] bytes) => throw ReadOnlyViolation();

    public override void AppendAllBytes(string path, ReadOnlySpan<byte> bytes) => throw ReadOnlyViolation();

    public override Task AppendAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task AppendAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override void AppendAllLines(string path, IEnumerable<string> contents) => throw ReadOnlyViolation();

    public override void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding) => throw ReadOnlyViolation();

    public override Task AppendAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task AppendAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override void AppendAllText(string path, string contents) => throw ReadOnlyViolation();

    public override void AppendAllText(string path, ReadOnlySpan<char> contents) => throw ReadOnlyViolation();

    public override void AppendAllText(string path, string contents, Encoding encoding) => throw ReadOnlyViolation();

    public override void AppendAllText(string path, ReadOnlySpan<char> contents, Encoding encoding) => throw ReadOnlyViolation();

    public override Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task AppendAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override StreamWriter AppendText(string path) => throw ReadOnlyViolation();

    public override void Copy(string sourceFileName, string destFileName) => throw ReadOnlyViolation();

    public override void Copy(string sourceFileName, string destFileName, bool overwrite) => throw ReadOnlyViolation();

    public override FileSystemStream Create(string path) => throw ReadOnlyViolation();

    public override FileSystemStream Create(string path, int bufferSize) => throw ReadOnlyViolation();

    public override FileSystemStream Create(string path, int bufferSize, FileOptions options) => throw ReadOnlyViolation();

    public override IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget) => throw ReadOnlyViolation();

    public override StreamWriter CreateText(string path) => throw ReadOnlyViolation();

    public override void Decrypt(string path) => throw ReadOnlyViolation();

    public override void Delete(string path) => throw ReadOnlyViolation();

    public override void Encrypt(string path) => throw ReadOnlyViolation();

    public override bool Exists(string path) => throw new NotImplementedException();

    public override FileAttributes GetAttributes(string path) => throw new NotImplementedException();

    public override FileAttributes GetAttributes(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override DateTime GetCreationTime(string path) => throw new NotImplementedException();

    public override DateTime GetCreationTime(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override DateTime GetCreationTimeUtc(string path) => throw new NotImplementedException();

    public override DateTime GetCreationTimeUtc(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override DateTime GetLastAccessTime(string path) => throw new NotImplementedException();

    public override DateTime GetLastAccessTime(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override DateTime GetLastAccessTimeUtc(string path) => throw new NotImplementedException();

    public override DateTime GetLastAccessTimeUtc(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override DateTime GetLastWriteTime(string path) => throw new NotImplementedException();

    public override DateTime GetLastWriteTime(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override DateTime GetLastWriteTimeUtc(string path) => throw new NotImplementedException();

    public override DateTime GetLastWriteTimeUtc(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override UnixFileMode GetUnixFileMode(string path) => throw new NotImplementedException();

    public override UnixFileMode GetUnixFileMode(SafeFileHandle fileHandle) => throw new NotImplementedException();

    public override void Move(string sourceFileName, string destFileName) => throw ReadOnlyViolation();

    public override void Move(string sourceFileName, string destFileName, bool overwrite) => throw ReadOnlyViolation();

    public override FileSystemStream Open(string path, FileMode mode) => throw ReadOnlyViolation();

    public override FileSystemStream Open(string path, FileStreamOptions options) => FileSystem.FileStream.New(path, options);

    public override FileSystemStream Open(string path, FileMode mode, FileAccess access) => FileSystem.FileStream.New(path, mode, access, FileShare.None);

    public override FileSystemStream Open(string path, FileMode mode, FileAccess access, FileShare share) => FileSystem.FileStream.New(path, mode, access, share);

    public override SafeFileHandle OpenHandle(string path, FileMode mode = FileMode.Open, FileAccess access = FileAccess.Read, FileShare share = FileShare.Read, FileOptions options = FileOptions.None, long preallocationSize = 0)
    {
        EnsureReadOnly(mode, access, options);
        return inner.File.OpenHandle(path, mode, access, share, options, preallocationSize);
    }

    public override FileSystemStream OpenRead(string path) => throw new NotImplementedException();

    public override StreamReader OpenText(string path) => throw new NotImplementedException();

    public override FileSystemStream OpenWrite(string path) => throw ReadOnlyViolation();

    public override byte[] ReadAllBytes(string path) => throw new NotImplementedException();

    public override Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override string[] ReadAllLines(string path) => throw new NotImplementedException();

    public override string[] ReadAllLines(string path, Encoding encoding) => throw new NotImplementedException();

    public override Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override string ReadAllText(string path) => throw new NotImplementedException();

    public override string ReadAllText(string path, Encoding encoding) => throw new NotImplementedException();

    public override Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override IEnumerable<string> ReadLines(string path) => throw new NotImplementedException();

    public override IEnumerable<string> ReadLines(string path, Encoding encoding) => throw new NotImplementedException();

    public override IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override IAsyncEnumerable<string> ReadLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public override void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName) => throw ReadOnlyViolation();

    public override void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName, bool ignoreMetadataErrors) => throw ReadOnlyViolation();

    public override IFileSystemInfo ResolveLinkTarget(string linkPath, bool returnFinalTarget) => throw new NotImplementedException();

    public override void SetAttributes(string path, FileAttributes fileAttributes) => throw ReadOnlyViolation();

    public override void SetAttributes(SafeFileHandle fileHandle, FileAttributes fileAttributes) => throw ReadOnlyViolation();

    public override void SetCreationTime(string path, DateTime creationTime) => throw ReadOnlyViolation();

    public override void SetCreationTime(SafeFileHandle fileHandle, DateTime creationTime) => throw ReadOnlyViolation();

    public override void SetCreationTimeUtc(string path, DateTime creationTimeUtc) => throw ReadOnlyViolation();

    public override void SetCreationTimeUtc(SafeFileHandle fileHandle, DateTime creationTimeUtc) => throw ReadOnlyViolation();

    public override void SetLastAccessTime(string path, DateTime lastAccessTime) => throw ReadOnlyViolation();

    public override void SetLastAccessTime(SafeFileHandle fileHandle, DateTime lastAccessTime) => throw ReadOnlyViolation();

    public override void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc) => throw ReadOnlyViolation();

    public override void SetLastAccessTimeUtc(SafeFileHandle fileHandle, DateTime lastAccessTimeUtc) => throw ReadOnlyViolation();

    public override void SetLastWriteTime(string path, DateTime lastWriteTime) => throw ReadOnlyViolation();

    public override void SetLastWriteTime(SafeFileHandle fileHandle, DateTime lastWriteTime) => throw ReadOnlyViolation();

    public override void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw ReadOnlyViolation();

    public override void SetLastWriteTimeUtc(SafeFileHandle fileHandle, DateTime lastWriteTimeUtc) => throw ReadOnlyViolation();

    public override void SetUnixFileMode(string path, UnixFileMode mode) => throw ReadOnlyViolation();

    public override void SetUnixFileMode(SafeFileHandle fileHandle, UnixFileMode mode) => throw ReadOnlyViolation();

    public override void WriteAllBytes(string path, byte[] bytes) => throw ReadOnlyViolation();

    public override void WriteAllBytes(string path, ReadOnlySpan<byte> bytes) => throw ReadOnlyViolation();

    public override Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override void WriteAllLines(string path, IEnumerable<string> contents) => throw ReadOnlyViolation();

    public override void WriteAllLines(string path, string[] contents) => throw ReadOnlyViolation();

    public override void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding) => throw ReadOnlyViolation();

    public override void WriteAllLines(string path, string[] contents, Encoding encoding) => throw ReadOnlyViolation();

    public override Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override void WriteAllText(string path, string contents) => throw ReadOnlyViolation();

    public override void WriteAllText(string path, ReadOnlySpan<char> contents) => throw ReadOnlyViolation();

    public override void WriteAllText(string path, string contents, Encoding encoding) => throw ReadOnlyViolation();

    public override void WriteAllText(string path, ReadOnlySpan<char> contents, Encoding encoding) => throw ReadOnlyViolation();

    public override Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public override Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();
}

internal sealed class ReadOnlyDirectory(ReadOnlyFileSystem fileSystem, IFileSystem inner) : DirectoryBase(fileSystem)
{
    public override IDirectoryInfo CreateDirectory(string path) => throw ReadOnlyViolation();

    public override IDirectoryInfo CreateDirectory(string path, UnixFileMode unixCreateMode) => throw ReadOnlyViolation();

    public override IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget) => throw ReadOnlyViolation();

    public override IDirectoryInfo CreateTempSubdirectory(string? prefix = null) => throw ReadOnlyViolation();

    public override void Delete(string path) => throw ReadOnlyViolation();

    public override void Delete(string path, bool recursive) => throw ReadOnlyViolation();

    public override IEnumerable<string> EnumerateDirectories(string path) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFiles(string path) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFiles(string path, string searchPattern) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions enumerationOptions) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFileSystemEntries(string path) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption) => throw new NotImplementedException();

    public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions) => throw new NotImplementedException();

    public override bool Exists(string path) => inner.Directory.Exists(path);

    public override DateTime GetCreationTime(string path) => throw new NotImplementedException();

    public override DateTime GetCreationTimeUtc(string path) => throw new NotImplementedException();

    public override string GetCurrentDirectory() => throw new NotImplementedException();

    public override string[] GetDirectories(string path) => throw new NotImplementedException();

    public override string[] GetDirectories(string path, string searchPattern) => throw new NotImplementedException();

    public override string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) => throw new NotImplementedException();

    public override string[] GetDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions) => throw new NotImplementedException();

    public override string GetDirectoryRoot(string path) => throw new NotImplementedException();

    public override string[] GetFiles(string path) => throw new NotImplementedException();

    public override string[] GetFiles(string path, string searchPattern) => throw new NotImplementedException();

    public override string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => throw new NotImplementedException();

    public override string[] GetFiles(string path, string searchPattern, EnumerationOptions enumerationOptions) => throw new NotImplementedException();

    public override string[] GetFileSystemEntries(string path) => throw new NotImplementedException();

    public override string[] GetFileSystemEntries(string path, string searchPattern) => throw new NotImplementedException();

    public override string[] GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption) => throw new NotImplementedException();

    public override string[] GetFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions) => throw new NotImplementedException();

    public override DateTime GetLastAccessTime(string path) => throw new NotImplementedException();

    public override DateTime GetLastAccessTimeUtc(string path) => throw new NotImplementedException();

    public override DateTime GetLastWriteTime(string path) => throw new NotImplementedException();

    public override DateTime GetLastWriteTimeUtc(string path) => throw new NotImplementedException();

    public override string[] GetLogicalDrives() => throw new NotImplementedException();

    public override IDirectoryInfo GetParent(string path) => throw new NotImplementedException();

    public override void Move(string sourceDirName, string destDirName) => throw ReadOnlyViolation();

    public override IFileSystemInfo ResolveLinkTarget(string linkPath, bool returnFinalTarget) => throw new NotImplementedException();

    public override void SetCreationTime(string path, DateTime creationTime) => throw ReadOnlyViolation();

    public override void SetCreationTimeUtc(string path, DateTime creationTimeUtc) => throw ReadOnlyViolation();

    public override void SetCurrentDirectory(string path) => throw new NotImplementedException();

    public override void SetLastAccessTime(string path, DateTime lastAccessTime) => throw ReadOnlyViolation();

    public override void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc) => throw ReadOnlyViolation();

    public override void SetLastWriteTime(string path, DateTime lastWriteTime) => throw ReadOnlyViolation();

    public override void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw ReadOnlyViolation();
}

internal sealed class ReadOnlyFileStreamFactory(ReadOnlyFileSystem fileSystem, IFileSystem inner) : IFileStreamFactory
{
    public IFileSystem FileSystem => fileSystem;

    public FileSystemStream New(SafeFileHandle handle, FileAccess access)
    {
        EnsureReadOnly(access);
        return inner.FileStream.New(handle, access);
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize)
    {
        EnsureReadOnly(access);
        return inner.FileStream.New(handle, access, bufferSize);
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync)
    {
        EnsureReadOnly(access);
        return inner.FileStream.New(handle, access, bufferSize, isAsync);
    }

    public FileSystemStream New(string path, FileMode mode) => throw ReadOnlyViolation();

    public FileSystemStream New(string path, FileStreamOptions options)
    {
        EnsureReadOnly(options.Mode, options.Access, options.Options);
        return inner.FileStream.New(path, options);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access)
    {
        EnsureReadOnly(mode, access);
        return inner.FileStream.New(path, mode, access);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share)
    {
        EnsureReadOnly(mode, access);
        return inner.FileStream.New(path, mode, access, share);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
    {
        EnsureReadOnly(mode, access);
        return inner.FileStream.New(path, mode, access, share, bufferSize);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
    {
        EnsureReadOnly(mode, access);
        return inner.FileStream.New(path, mode, access, share, bufferSize, useAsync);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        EnsureReadOnly(mode, access, options);
        return inner.FileStream.New(path, mode, access, share, bufferSize, options);
    }

    public FileSystemStream Wrap(FileStream fileStream) => throw new NotImplementedException();
}

internal sealed class UnimplementedFileInfoFactory(ReadOnlyFileSystem fileSystem) : IFileInfoFactory
{
    public IFileSystem FileSystem => fileSystem;

    public IFileInfo New(string fileName) => throw new NotImplementedException();

    public IFileInfo? Wrap(FileInfo? fileInfo) => throw new NotImplementedException();
}

internal sealed class UnimplementedDirectoryInfoFactory(ReadOnlyFileSystem fileSystem) : IDirectoryInfoFactory
{
    public IFileSystem FileSystem => fileSystem;

    public IDirectoryInfo New(string path) => throw new NotImplementedException();

    public IDirectoryInfo? Wrap(DirectoryInfo? directoryInfo) => throw new NotImplementedException();
}

internal sealed class ReadOnlyRandomAccess(ReadOnlyFileSystem fileSystem) : IRandomAccess
{
    public IFileSystem FileSystem => fileSystem;

    public void FlushToDisk(SafeFileHandle handle) => throw ReadOnlyViolation();

    public long GetLength(SafeFileHandle handle) => throw new NotImplementedException();

    public int Read(SafeFileHandle handle, Span<byte> buffer, long fileOffset) => throw new NotImplementedException();

    public long Read(SafeFileHandle handle, IReadOnlyList<Memory<byte>> buffers, long fileOffset) => throw new NotImplementedException();

    public ValueTask<int> ReadAsync(SafeFileHandle handle, Memory<byte> buffer, long fileOffset, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public ValueTask<long> ReadAsync(SafeFileHandle handle, IReadOnlyList<Memory<byte>> buffers, long fileOffset, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public void SetLength(SafeFileHandle handle, long length) => throw ReadOnlyViolation();

    public void Write(SafeFileHandle handle, ReadOnlySpan<byte> buffer, long fileOffset) => throw ReadOnlyViolation();

    public void Write(SafeFileHandle handle, IReadOnlyList<ReadOnlyMemory<byte>> buffers, long fileOffset) => throw ReadOnlyViolation();

    public ValueTask WriteAsync(SafeFileHandle handle, ReadOnlyMemory<byte> buffer, long fileOffset, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();

    public ValueTask WriteAsync(SafeFileHandle handle, IReadOnlyList<ReadOnlyMemory<byte>> buffers, long fileOffset, CancellationToken cancellationToken = default) => throw ReadOnlyViolation();
}
