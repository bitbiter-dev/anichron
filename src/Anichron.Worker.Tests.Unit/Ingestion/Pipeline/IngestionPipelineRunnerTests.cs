using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Tests.Unit.TestDoubles;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Ingestion.Pipeline;

public sealed class IngestionPipelineRunnerTests
{
    private sealed class TestFixture
    {
        public MockFileSystem FileSystem { get; init; } = new();
        public IProxyDirectoryStrategy DirectoryStrategy { get; }

        public TestFixture()
        {
            DirectoryStrategy = Substitute.For<IProxyDirectoryStrategy>();
            DirectoryStrategy.GetDirectory(Arg.Any<Guid>(), Arg.Any<string>()).Returns("ab/cd");
        }

        public IngestionPipelineRunner Build(params IIngestionMiddleware[] middlewares)
            => new(middlewares,
                   DirectoryStrategy,
                   FileSystem,
                   Substitute.For<ILogger<IngestionPipelineRunner>>());
    }

    private static IngestionContext MakeContext() => new()
    {
        Item = new SingleFileItem("/abs/file.jpg", "file.jpg", MediaType.Image),
        Config = new UserStorageConfig { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RootPath = "/abs" },
        AssetId = Guid.NewGuid(),
        ContentHash = "0123456789abcdef",
    };

    // ==========================================================================
    // Constructor — validation
    // ==========================================================================

    [Fact]
    public void Constructor_DuplicateOrders_ThrowsInvalidOperationException()
    {
        var first = Substitute.For<IIngestionMiddleware>();
        first.Order.Returns(10);
        first.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        var second = Substitute.For<IIngestionMiddleware>();
        second.Order.Returns(10);
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);

        var act = () => new TestFixture().Build(first, second);

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate middleware orders: 10");
    }

    [Fact]
    public void Constructor_SeveralDuplicateOrders_ListsThemAll()
    {
        static IIngestionMiddleware Middleware(int order)
        {
            var middleware = Substitute.For<IIngestionMiddleware>();
            middleware.Order.Returns(order);
            return middleware;
        }

        var act = () => new TestFixture().Build(Middleware(10), Middleware(10), Middleware(20), Middleware(20));

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate middleware orders: 10, 20");
    }

    [Fact]
    public void Constructor_UniqueOrders_DoesNotThrow()
    {
        var first = Substitute.For<IIngestionMiddleware>();
        first.Order.Returns(10);
        first.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
        var second = Substitute.For<IIngestionMiddleware>();
        second.Order.Returns(20);
        second.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);

        var act = () => new TestFixture().Build(first, second);

        act.Should().NotThrow();
    }

    // ==========================================================================
    // RunAsync — ordering
    // ==========================================================================

    [Fact]
    public async Task RunAsync_MiddlewaresRunInAscendingOrderAsync()
    {
        var invoked = new List<int>();
        var low = new StubMiddleware(10, invoked);
        var high = new StubMiddleware(20, invoked);

        // Intentionally register in reverse to verify sort
        var runner = new TestFixture().Build(high, low);

        await runner.RunAsync(MakeContext(), CancellationToken.None);

        invoked.Should().Equal(10, 20);
    }

    // ==========================================================================
    // RunAsync — compensation on failure
    // ==========================================================================

    [Fact]
    public async Task RunAsync_StepThrows_DeletesProxiesRegisteredDuringTheAttemptAsync()
    {
        var fixture = new TestFixture();
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg", "ab/cd/preview.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/thumbnail.jpg").Should().BeFalse();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/preview.jpg").Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_StepThrows_PropagatesTheOriginalExceptionAsync()
    {
        var fixture = new TestFixture();
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task RunAsync_StepThrows_DeletesTemporarySiblingsAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/data/proxies/ab/cd/video_720p.mp4.tmp", new MockFileData([0x01]));
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/video_720p.mp4.tmp").Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_StepThrows_RemovesTheEmptiedDirectoryAsync()
    {
        var fixture = new TestFixture();
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.Directory.Exists("/data/proxies/ab/cd").Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_StepThrows_KeepsDirectoryHoldingFilesItDidNotWriteAsync()
    {
        var fixture = new TestFixture();
        // A concurrent attempt on the same content shares the directory.
        fixture.FileSystem.AddFile("/data/proxies/ab/cd/blurhash.txt", new MockFileData([0x01]));
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/blurhash.txt").Should().BeTrue();
        fixture.FileSystem.Directory.Exists("/data/proxies/ab/cd").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_StepThrowsBeforeRegisteringAnyProxy_RemovesTheEmptyDirectoryAsync()
    {
        var fixture = new TestFixture();
        var runner = fixture.Build(
            new ProxyDirectoryCreatingMiddleware(10, fixture.FileSystem),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.Directory.Exists("/data/proxies/ab/cd").Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_StepThrowsBeforeRegisteringAnyProxy_DeletesTemporaryFilesAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/data/proxies/ab/cd/video_720p.mp4.tmp", new MockFileData([0x01]));
        var runner = fixture.Build(
            new ProxyDirectoryCreatingMiddleware(10, fixture.FileSystem),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/video_720p.mp4.tmp").Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_Succeeds_DeletesNothingAsync()
    {
        var fixture = new TestFixture();
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg", "ab/cd/preview.jpg"));

        await runner.RunAsync(MakeContext(), CancellationToken.None);

        fixture.FileSystem.FileExists("/data/proxies/ab/cd/thumbnail.jpg").Should().BeTrue();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/preview.jpg").Should().BeTrue();
    }

    // ==========================================================================
    // RunAsync — cancellation is not failure
    // ==========================================================================

    [Fact]
    public async Task RunAsync_CancellationRequested_KeepsCompletedProxiesAsync()
    {
        var fixture = new TestFixture();
        using var cts = new CancellationTokenSource();
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg"),
            new CancellingMiddleware(20, cts));

        var act = () => runner.RunAsync(MakeContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/thumbnail.jpg").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_CancellationRequested_PropagatesTheCancellationAsync()
    {
        var fixture = new TestFixture();
        using var cts = new CancellationTokenSource();
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/thumbnail.jpg"),
            new CancellingMiddleware(20, cts));

        var act = () => runner.RunAsync(MakeContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ==========================================================================
    // RunAsync — cleanup never masks the original failure
    // ==========================================================================

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    public async Task RunAsync_DirectoryCleanupFails_StillPropagatesTheOriginalExceptionAsync(string failure)
    {
        Exception cleanupFailure = failure == "io" ? new IOException("io") : new UnauthorizedAccessException("access");
        var fixture = new TestFixture { FileSystem = new EnumerationFailingFileSystem(() => cleanupFailure) };
        fixture.FileSystem.AddDirectory("/data/proxies/ab/cd");
        var runner = fixture.Build(new ThrowingMiddleware(20, new InvalidOperationException("original")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("original");
    }

    [Fact]
    public async Task RunAsync_DirectoryCleanupFailsUnexpectedly_SurfacesThatFailureAsync()
    {
        var fixture = new TestFixture { FileSystem = new EnumerationFailingFileSystem(() => new FormatException("unexpected")) };
        fixture.FileSystem.AddDirectory("/data/proxies/ab/cd");
        var runner = fixture.Build(new ThrowingMiddleware(20, new InvalidOperationException("original")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task RunAsync_DeleteFails_StillPropagatesTheOriginalExceptionAsync()
    {
        var fixture = new TestFixture { FileSystem = new DeleteFailingFileSystem("/data/proxies/ab/cd/preview.jpg") };
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/preview.jpg", "ab/cd/thumbnail.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task RunAsync_DeleteFails_StillDeletesTheRemainingProxiesAsync()
    {
        var fixture = new TestFixture { FileSystem = new DeleteFailingFileSystem("/data/proxies/ab/cd/preview.jpg") };
        var runner = fixture.Build(
            new ProxyWritingMiddleware(10, fixture.FileSystem, "ab/cd/preview.jpg", "ab/cd/thumbnail.jpg"),
            new ThrowingMiddleware(20, new InvalidOperationException("boom")));

        var act = () => runner.RunAsync(MakeContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/thumbnail.jpg").Should().BeFalse();
        fixture.FileSystem.FileExists("/data/proxies/ab/cd/preview.jpg").Should().BeTrue();
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private sealed class StubMiddleware(int order, List<int> tracker) : IIngestionMiddleware
    {
        public int Order => order;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            tracker.Add(order);
            await next(context, ct);
        }
    }

    private sealed class ProxyWritingMiddleware(
        int order,
        IFileSystem fileSystem,
        params string[] relativePaths) : IIngestionMiddleware
    {
        public int Order => order;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            foreach (var relativePath in relativePaths)
            {
                var absolutePath = $"/data/proxies/{relativePath}";
                fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(absolutePath)!);
                await fileSystem.File.WriteAllBytesAsync(absolutePath, [0x01], ct);
                context.AddProxyFile(new ProxyFile
                {
                    Id = Guid.NewGuid(),
                    AssetId = context.AssetId,
                    ProxyPath = relativePath,
                    ProxyType = ProxyType.Thumbnail,
                });
            }

            await next(context, ct);
        }
    }

    private sealed class ProxyDirectoryCreatingMiddleware(int order, IFileSystem fileSystem)
        : IIngestionMiddleware
    {
        public int Order => order;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            fileSystem.Directory.CreateDirectory("/data/proxies/ab/cd");
            await next(context, ct);
        }
    }

    private sealed class ThrowingMiddleware(int order, Exception exception) : IIngestionMiddleware
    {
        public int Order => order;
        public bool CanInvoke(IngestionContext context) => true;

        public Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            _ = (context, next, ct);
            throw exception;
        }
    }

    private sealed class CancellingMiddleware(int order, CancellationTokenSource cts) : IIngestionMiddleware
    {
        public int Order => order;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            _ = (context, next);
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }
    }
}
