using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class FileIngestionPipelineTests
{
    private static UserStorageConfig MakeConfig(string rootPath = "/nas") => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        RootPath = rootPath,
    };

    private sealed class TestFixture
    {
        public MockFileSystem FileSystem { get; } = new();
        public List<IngestionContext> ProcessedContexts { get; } = [];

        public FileIngestionPipeline Build(int maxConcurrentFiles = 2)
        {
            var capturingMiddleware = Substitute.For<IIngestionMiddleware>();
            capturingMiddleware.Order.Returns(10);
            capturingMiddleware.CanInvoke(Arg.Any<IngestionContext>()).Returns(true);
            capturingMiddleware.InvokeAsync(
                    Arg.Any<IngestionContext>(),
                    Arg.Any<IngestionDelegate>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    lock (ProcessedContexts)
                        ProcessedContexts.Add(callInfo.ArgAt<IngestionContext>(0));
                    return Task.CompletedTask;
                });

            return BuildWithMiddlewares([capturingMiddleware], maxConcurrentFiles);
        }

        public FileIngestionPipeline BuildWithMiddlewares(
            IEnumerable<IIngestionMiddleware> middlewares,
            int maxConcurrentFiles = 2)
        {
            var directoryStrategy = Substitute.For<IProxyDirectoryStrategy>();
            directoryStrategy.GetDirectory(Arg.Any<Guid>(), Arg.Any<string>()).Returns("ab/cd");

            var runner = new IngestionPipelineRunner(
                middlewares,
                directoryStrategy,
                FileSystem,
                Options.Create(new WorkerSettings { ProxyPath = "/proxies" }),
                Substitute.For<ILogger<IngestionPipelineRunner>>());

            var serviceProvider = Substitute.For<IServiceProvider>();
            serviceProvider.GetService(typeof(IIngestionPipelineRunner)).Returns(runner);

            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(serviceProvider);

            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(_ => scope);

            var options = Options.Create(new WorkerSettings { MaxConcurrentFiles = maxConcurrentFiles });
            return new FileIngestionPipeline(
                scopeFactory,
                FileSystem,
                new LivePhotoLinker(FileSystem),
                options,
                Substitute.For<IGuidFactory>(),
                Substitute.For<ILogger<FileIngestionPipeline>>());
        }
    }

    // ==========================================================================
    // Live Photo pairing — producer
    // ==========================================================================

    [Fact]
    public async Task RunAsync_HeicWithMatchingMovSibling_EnqueuesLivePhotoPairItemAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/IMG_0001.HEIC", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/IMG_0001.MOV", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle()
            .Which.Item.Should().BeOfType<LivePhotoPairItem>();
    }

    [Fact]
    public async Task RunAsync_HeicWithMatchingMovSibling_PairItemContainsCorrectPathsAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/IMG_0001.HEIC", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/IMG_0001.MOV", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        var pair = fixture.ProcessedContexts.Single().Item.Should()
            .BeOfType<LivePhotoPairItem>().Subject;
        pair.AbsolutePath.Should().Be("/nas/IMG_0001.HEIC");
        pair.MovAbsolutePath.Should().Be("/nas/IMG_0001.MOV");
    }

    [Fact]
    public async Task RunAsync_HeicWithoutMovSibling_EnqueuesSingleFileItemAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.HEIC", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle()
            .Which.Item.Should().BeOfType<SingleFileItem>();
    }

    [Fact]
    public async Task RunAsync_PairedMovFile_IsNotEnqueuedAsSeparateSingleFileItemAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/IMG_0001.HEIC", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/IMG_0001.MOV", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_MovWithoutHeicSibling_EnqueuesSingleFileItemAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/video.MOV", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle()
            .Which.Item.Should().BeOfType<SingleFileItem>()
            .Which.MediaType.Should().Be(MediaType.Video);
    }

    // ==========================================================================
    // Unsupported file types
    // ==========================================================================

    [Fact]
    public async Task RunAsync_UnsupportedFileExtension_IsSkippedAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/document.pdf", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MixedSupportedAndUnsupported_OnlySupportedFilesAreProcessedAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/document.pdf", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle()
            .Which.Item.Should().BeOfType<SingleFileItem>()
            .Which.AbsolutePath.Should().Be("/nas/photo.jpg");
    }

    // ==========================================================================
    // Relative paths
    // ==========================================================================

    [Fact]
    public async Task RunAsync_SingleFileItem_RelativePathIsRelativeToRootPathAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/2024/summer/beach.jpg", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        var item = fixture.ProcessedContexts.Single().Item.Should()
            .BeOfType<SingleFileItem>().Subject;
        item.RelativePath.Should().Be("2024/summer/beach.jpg");
    }

    // ==========================================================================
    // Cross-directory isolation
    // ==========================================================================

    [Fact]
    public async Task RunAsync_SameBaseNameInDifferentDirectories_NotPairedAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a/IMG_0001.HEIC", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/b/IMG_0001.MOV", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        fixture.ProcessedContexts.Should().HaveCount(2);
        fixture.ProcessedContexts.Should().AllSatisfy(context =>
            context.Item.Should().BeOfType<SingleFileItem>());
    }

    // ==========================================================================
    // Empty directory
    // ==========================================================================

    [Fact]
    public async Task RunAsync_EmptyDirectory_CompletesWithoutExceptionAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddDirectory("/nas");

        var pipeline = fixture.Build();
        var act = async () => await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        fixture.ProcessedContexts.Should().BeEmpty();
    }

    // ==========================================================================
    // Shutdown
    // ==========================================================================

    [Fact]
    public async Task RunAsync_CancellationRequestedWhileIngesting_StopsWithoutTakingTheNextFileAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/b.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/c.jpg", new MockFileData([]));
        using var cts = new CancellationTokenSource();
        var seen = new List<IngestionContext>();

        var pipeline = fixture.BuildWithMiddlewares([new CancellingMiddleware(cts, seen)], maxConcurrentFiles: 1);
        var act = async () => await pipeline.RunAsync(MakeConfig("/nas"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        seen.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAsync_ItemFails_StillIngestsTheRemainingFilesAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/b.jpg", new MockFileData([]));
        var seen = new List<IngestionContext>();

        var pipeline = fixture.BuildWithMiddlewares(
            [new FailingOnFirstItemMiddleware(seen)], maxConcurrentFiles: 1);
        var act = async () => await pipeline.RunAsync(MakeConfig("/nas"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        seen.Should().HaveCount(2);
    }

    // ==========================================================================
    // Producer exception
    // ==========================================================================

    [Fact]
    public async Task RunAsync_RootDirectoryDoesNotExist_ThrowsDirectoryNotFoundExceptionAsync()
    {
        var fixture = new TestFixture();

        var pipeline = fixture.Build();
        var act = async () => await pipeline.RunAsync(MakeConfig("/nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    // ==========================================================================
    // Helpers
    // ==========================================================================

    private sealed class CancellingMiddleware(CancellationTokenSource cts, List<IngestionContext> seen)
        : IIngestionMiddleware
    {
        public int Order => 10;
        public bool CanInvoke(IngestionContext context) => true;

        public async Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            _ = next;
            seen.Add(context);
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }
    }

    private sealed class FailingOnFirstItemMiddleware(List<IngestionContext> seen) : IIngestionMiddleware
    {
        public int Order => 10;
        public bool CanInvoke(IngestionContext context) => true;

        public Task InvokeAsync(IngestionContext context, IngestionDelegate next, CancellationToken ct)
        {
            _ = (next, ct);
            seen.Add(context);
            if (seen.Count == 1)
                throw new InvalidOperationException("boom");

            return Task.CompletedTask;
        }
    }
}
