using Anichron.API.Services;
using Anichron.Core.Domain;
using Anichron.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.API.Tests.Unit.Services;

public sealed class BlurhashReaderTests
{
    private const string BlurhashPath = "01/3fa85f6457174562b3fc2c963f66afa6-0123456789abcdef/blurhash.txt";
    private const string ThumbnailPath = "01/3fa85f6457174562b3fc2c963f66afa6-0123456789abcdef/thumbnail.jpg";

    private readonly ILogger<BlurhashReader> logger = Substitute.For<ILogger<BlurhashReader>>();

    public BlurhashReaderTests() => logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

    private static MediaAsset Asset(params ProxyFile[] proxies) => new() { ProxyFiles = [.. proxies] };

    private static ProxyFile Proxy(ProxyType type, string path) => new() { ProxyType = type, ProxyPath = path };

    [Fact]
    public async Task ReadAsync_ReturnsTheBlurhashFilesText_ThroughTheReadOnlyFileSystem()
    {
        var inner = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/data/proxies/" + ThumbnailPath] = new("not a blurhash"),
            ["/data/proxies/" + BlurhashPath] = new("LEHV6nWB2yk8"),
        });
        var testee = new BlurhashReader(new ReadOnlyFileSystem(inner), logger);
        var asset = Asset(Proxy(ProxyType.Thumbnail, ThumbnailPath), Proxy(ProxyType.BlurHash, BlurhashPath));

        var blurhash = await testee.ReadAsync(asset, TestContext.Current.CancellationToken);

        blurhash.Should().Be("LEHV6nWB2yk8");
    }

    [Fact]
    public async Task ReadAsync_WithoutABlurhashProxy_ReturnsNullWithoutTouchingTheFileSystem()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var testee = new BlurhashReader(fileSystem, logger);

        var blurhash = await testee.ReadAsync(
            Asset(Proxy(ProxyType.Thumbnail, ThumbnailPath)), TestContext.Current.CancellationToken);

        blurhash.Should().BeNull();
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenTheBlurhashFileIsMissing_ReturnsNullAndWarns()
    {
        var testee = new BlurhashReader(new ReadOnlyFileSystem(new MockFileSystem()), logger);

        var blurhash = await testee.ReadAsync(
            Asset(Proxy(ProxyType.BlurHash, BlurhashPath)), TestContext.Current.CancellationToken);

        blurhash.Should().BeNull();
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Is<Exception?>(ex => ex is FileNotFoundException),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Fact]
    public async Task ReadAsync_WhenTheBlurhashFileIsNotReadable_ReturnsNullAndWarns()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.File.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new UnauthorizedAccessException());
        var testee = new BlurhashReader(fileSystem, logger);

        var blurhash = await testee.ReadAsync(
            Asset(Proxy(ProxyType.BlurHash, BlurhashPath)), TestContext.Current.CancellationToken);

        blurhash.Should().BeNull();
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Is<Exception?>(ex => ex is UnauthorizedAccessException),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }
}
