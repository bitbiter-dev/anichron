using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Crawling;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Settings;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace Anichron.Worker.Tests.Unit.Crawling;

public sealed class FileIngestionPipelineTests
{
    private static IWorkProgress NoProgress => Substitute.For<IWorkProgress>();

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

        public string ExcludedDirectories { get; init; } = string.Empty;
        public string? UnreadableDirectory { get; init; }

        private IFileSystem CrawlFileSystem()
        {
            if (UnreadableDirectory is null)
                return FileSystem;

            var directory = Substitute.For<IDirectory>();
            directory.EnumerateFiles(Arg.Any<string>()).Returns(call => Readable(call.Arg<string>()).EnumerateFiles(call.Arg<string>()));
            directory.EnumerateDirectories(Arg.Any<string>())
                .Returns(call => Readable(call.Arg<string>()).EnumerateDirectories(call.Arg<string>()));
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.Directory.Returns(directory);
            fileSystem.Path.Returns(FileSystem.Path);
            return fileSystem;

            IDirectory Readable(string path)
                => path == UnreadableDirectory
                    ? throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.")
                    : FileSystem.Directory;
        }

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
            => FileIngestionPipelines.Build(
                middlewares,
                FileSystem,
                new WorkerSettings { MaxConcurrentFiles = maxConcurrentFiles, ExcludedDirectories = ExcludedDirectories },
                CrawlFileSystem());
    }

    // ==========================================================================
    // Exclusions
    // ==========================================================================

    private static IEnumerable<string> ProcessedRelativePaths(TestFixture fixture)
        => fixture.ProcessedContexts.Select(c => c.Item.RelativePath).Order(StringComparer.Ordinal);

    [Fact]
    public async Task RunAsync_NasSystemFoldersAndDotFiles_OnlyRealPhotosAreProcessedAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/IMG_0001.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/2024/IMG_0002.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/@eaDir/IMG_0001.jpg/SYNOPHOTO_THUMB_XL.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/2024/@eaDir/IMG_0002.jpg/SYNOPHOTO_THUMB_M.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/#recycle/old.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/.hidden/x.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/._IMG_0001.jpg", new MockFileData([]));

        await fixture.Build().RunAsync(MakeConfig(), NoProgress, CancellationToken.None);

        ProcessedRelativePaths(fixture).Should().Equal("2024/IMG_0002.jpg", "IMG_0001.jpg");
    }

    [Fact]
    public async Task RunAsync_ConfiguredExcludedDirectory_IsSkippedAsync()
    {
        var fixture = new TestFixture { ExcludedDirectories = "Lightroom Previews" };
        fixture.FileSystem.AddFile("/nas/IMG_0001.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/Lightroom Previews/preview.jpg", new MockFileData([]));

        await fixture.Build().RunAsync(MakeConfig(), NoProgress, CancellationToken.None);

        ProcessedRelativePaths(fixture).Should().Equal("IMG_0001.jpg");
    }

    [Fact]
    public async Task RunAsync_UnreadableSubfolder_IsSkippedAndTheCrawlContinuesAsync()
    {
        var fixture = new TestFixture { UnreadableDirectory = "/nas/locked" };
        fixture.FileSystem.AddFile("/nas/IMG_0001.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/locked/IMG_0002.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/zz-after/IMG_0003.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/aa-before/IMG_0004.jpg", new MockFileData([]));

        await fixture.Build().RunAsync(MakeConfig(), NoProgress, CancellationToken.None);

        ProcessedRelativePaths(fixture).Should().Equal("IMG_0001.jpg", "aa-before/IMG_0004.jpg", "zz-after/IMG_0003.jpg");
    }

    [Fact]
    public async Task RunAsync_RootWhoseOwnNameIsExcluded_IsStillCrawledAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/Photos Library.photoslibrary/IMG_0001.jpg", new MockFileData([]));

        await fixture.Build().RunAsync(MakeConfig("/nas/Photos Library.photoslibrary"), NoProgress, CancellationToken.None);

        ProcessedRelativePaths(fixture).Should().Equal("IMG_0001.jpg");
    }

    [Fact]
    public async Task RunAsync_LivePhotoInNestedFolderNextToEaDir_IsStillPairedAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/2024/06/IMG_0001.HEIC", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/2024/06/IMG_0001.MOV", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/2024/06/@eaDir/IMG_0001.HEIC/SYNOPHOTO_THUMB_XL.jpg", new MockFileData([]));

        await fixture.Build().RunAsync(MakeConfig(), NoProgress, CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle()
            .Which.Item.Should().BeOfType<LivePhotoPairItem>();
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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

        fixture.ProcessedContexts.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_MovWithoutHeicSibling_EnqueuesSingleFileItemAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/video.MOV", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

        fixture.ProcessedContexts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MixedSupportedAndUnsupported_OnlySupportedFilesAreProcessedAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/photo.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/document.pdf", new MockFileData([]));

        var pipeline = fixture.Build();
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

        fixture.ProcessedContexts.Should().HaveCount(2);
        fixture.ProcessedContexts.Should().AllSatisfy(context =>
            context.Item.Should().BeOfType<SingleFileItem>());
    }

    // ==========================================================================
    // Empty directory
    // ==========================================================================

    [Fact]
    public async Task RunAsync_SingleConsumer_StillIngestsEveryFileAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData("a"));
        fixture.FileSystem.AddFile("/nas/sub/b.jpg", new MockFileData("b"));

        await fixture.Build(maxConcurrentFiles: 1).RunAsync(MakeConfig(), NoProgress, CancellationToken.None);

        fixture.ProcessedContexts.Select(c => c.Item.RelativePath)
            .Should().BeEquivalentTo("a.jpg", Path.Combine("sub", "b.jpg"));
    }

    [Fact]
    public async Task RunAsync_EmptyDirectory_CompletesWithoutExceptionAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddDirectory("/nas");

        var pipeline = fixture.Build();
        var act = async () => await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

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
        var act = async () => await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, cts.Token);

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
        var act = async () => await pipeline.RunAsync(MakeConfig("/nas"), NoProgress, CancellationToken.None);

        await act.Should().NotThrowAsync();
        seen.Should().HaveCount(2);
    }

    // ==========================================================================
    // Counts and progress
    // ==========================================================================

    [Fact]
    public async Task RunAsync_CountsItemsFoundProcessedAndFailed_ALivePhotoPairAsOneAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/b.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/IMG_0001.HEIC", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/IMG_0001.MOV", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/notes.txt", new MockFileData([]));

        var progress = new RecordingProgress();

        var run = await fixture.BuildWithMiddlewares([new FailingOnFirstItemMiddleware([])], maxConcurrentFiles: 1)
            .RunAsync(MakeConfig(), progress, CancellationToken.None);

        run.Result.Should().Be(new CrawlResult(Found: 3, Processed: 2, Failed: 1));
        progress.Reports[^1].Should().Be(new WorkProgress(3, 3, "3 found, 2 processed, 1 failed"));
    }

    [Fact]
    public async Task RunAsync_CountsTheDuplicateCopiesOfItemsThatSucceededOnlyAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/b.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/c.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/d.jpg", new MockFileData([]));
        var middleware = new ScriptedClassificationMiddleware(
            new ScriptedItem(FileOutcome.DuplicateCopy),
            new ScriptedItem(FileOutcome.DuplicateCopy, FileOutcome.DuplicateCopy),
            new ScriptedItem(FileOutcome.DuplicateCopy, Fails: true),
            new ScriptedItem(FileOutcome.Unchanged));

        var result = await fixture.BuildWithMiddlewares([middleware], maxConcurrentFiles: 1)
            .RunAsync(MakeConfig(), NoProgress, CancellationToken.None);

        result.Should().Be(new CrawlRun(new CrawlResult(Found: 4, Processed: 3, Failed: 1), DuplicateCopies: 3));
    }

    [Fact]
    public async Task RunAsync_ReportsProgressWithoutATotalUntilTheWalkEndsAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/sub/b.jpg", new MockFileData([]));
        var progress = new RecordingProgress();

        await fixture.Build(maxConcurrentFiles: 1).RunAsync(MakeConfig(), progress, CancellationToken.None);

        progress.Reports[0].Should().Be(new WorkProgress(0, null, "1 found, 0 processed, 0 failed"));
        progress.Reports[^1].Should().Be(new WorkProgress(2, 2, "2 found, 2 processed, 0 failed"));
        progress.Reports.Should().OnlyContain(report => report.Done <= 2);
    }

    [Fact]
    public async Task RunAsync_SecondRunOverTheSameFolder_ConvergesOnTheSameCountsAsync()
    {
        var fixture = new TestFixture();
        fixture.FileSystem.AddFile("/nas/a.jpg", new MockFileData([]));
        fixture.FileSystem.AddFile("/nas/sub/b.jpg", new MockFileData([]));
        var pipeline = fixture.Build();
        var config = MakeConfig();

        var first = await pipeline.RunAsync(config, NoProgress, CancellationToken.None);
        var second = await pipeline.RunAsync(config, NoProgress, CancellationToken.None);

        (first, second).Should().Be((new CrawlRun(new CrawlResult(2, 2, 0), 0), new CrawlRun(new CrawlResult(2, 2, 0), 0)));
    }

    private sealed class RecordingProgress : IWorkProgress
    {
        public List<WorkProgress> Reports { get; } = [];

        public void Report(long done, long? total = null, string? note = null)
        {
            lock (Reports)
                Reports.Add(new WorkProgress(done, total, note));
        }
    }

    // ==========================================================================
    // Producer exception
    // ==========================================================================

    [Fact]
    public async Task RunAsync_RootDirectoryDoesNotExist_ThrowsDirectoryNotFoundExceptionAsync()
    {
        var fixture = new TestFixture();

        var pipeline = fixture.Build();
        var act = async () => await pipeline.RunAsync(MakeConfig("/nonexistent"), NoProgress, CancellationToken.None);

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
