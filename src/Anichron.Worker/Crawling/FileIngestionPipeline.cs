using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using NodaTime;
using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Threading.Channels;

namespace Anichron.Worker.Crawling;

internal interface IFileIngestionPipeline
{
    Task<CrawlRun> RunAsync(UserStorageConfig config, FailingFiles failing, IWorkProgress progress, CancellationToken ct);
}

internal sealed partial class FileIngestionPipeline(
    IServiceScopeFactory scopeFactory,
    IFileSystem fileSystem,
    ILivePhotoLinker livePhotoLinker,
    CrawlExclusions exclusions,
    IOptions<WorkerSettings> workerOptions,
    IGuidFactory guidFactory,
    ILogger<FileIngestionPipeline> logger) : IFileIngestionPipeline
{
    private readonly WorkerSettings settings = workerOptions.Value;

    public async Task<CrawlRun> RunAsync(
        UserStorageConfig config, FailingFiles failing, IWorkProgress progress, CancellationToken ct)
    {
        var tally = new CrawlTally(progress);
        var retries = new ConcurrentQueue<IngestionItem>();
        await RunPassAsync(config, writer => ProduceAsync(config, failing, writer, tally, ct), failing, tally, retries, ct);
        await RunPassAsync(config, writer => RequeueAsync(retries, writer, ct), failing, tally, null, ct);
        return tally.Run;
    }

    private Task RunPassAsync(
        UserStorageConfig config,
        Func<ChannelWriter<IngestionItem>, Task> produce,
        FailingFiles failing,
        CrawlTally tally,
        ConcurrentQueue<IngestionItem>? retryLater,
        CancellationToken ct)
    {
        var channel = Channel.CreateBounded<IngestionItem>(
            // Stryker disable once Arithmetic,Initializer
            new BoundedChannelOptions(settings.MaxConcurrentFiles * 2)
            {
                // Stryker disable once Boolean
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        var producer = CompleteAfterAsync(produce, channel.Writer);
        var consumers = Enumerable
            .Range(0, settings.MaxConcurrentFiles)
            .Select(workerIndex => ConsumeAsync(config, channel.Reader, workerIndex, failing, tally, retryLater, ct))
            .ToArray();

        return Task.WhenAll([producer, .. consumers]);
    }

    private static async Task CompleteAfterAsync(
        Func<ChannelWriter<IngestionItem>, Task> produce, ChannelWriter<IngestionItem> writer)
    {
        try
        {
            await produce(writer);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task RequeueAsync(
        IEnumerable<IngestionItem> items, ChannelWriter<IngestionItem> writer, CancellationToken ct)
    {
        foreach (var item in items)
            await writer.WriteAsync(item, ct);
    }

    private async Task ProduceAsync(
        UserStorageConfig config,
        FailingFiles failing,
        ChannelWriter<IngestionItem> writer,
        CrawlTally tally,
        CancellationToken ct)
    {
        var pending = new Stack<string>([config.RootPath]);
        while (pending.TryPop(out var directory))
        {
            var (files, subdirectories) = ListDirectory(directory, config.RootPath, tally);
            await EnqueueDirectoryItemsAsync(files, config.RootPath, failing, writer, tally, ct);
            foreach (var subdirectory in subdirectories)
                pending.Push(subdirectory);
        }

        tally.Walked();
    }

    private (List<string> Files, List<string> Subdirectories) ListDirectory(string directory, string rootPath, CrawlTally tally)
    {
        try
        {
            return (
                [.. fileSystem.Directory.EnumerateFiles(directory)
                    .Where(file => !CrawlExclusions.IsExcludedFile(fileSystem.Path.GetFileName(file))
                        && !SkipsAsLink(file, rootPath, tally))],
                [.. fileSystem.Directory.EnumerateDirectories(directory)
                    .Where(subdirectory => !exclusions.IsExcludedDirectory(fileSystem.Path.GetFileName(subdirectory))
                        && !SkipsAsLink(subdirectory, rootPath, tally))]);
        }
        catch (Exception ex) when (directory != rootPath && ex is UnauthorizedAccessException or IOException)
        {
            Log.DirectoryUnreadable(logger, directory, ex);
            tally.FolderSkipped();
            return ([], []);
        }
    }

    private bool SkipsAsLink(string path, string rootPath, CrawlTally tally)
    {
        if (fileSystem.FileInfo.New(path).LinkTarget is null)
            return false;

        Log.LinkSkipped(logger, fileSystem.Path.GetRelativePath(rootPath, path));
        tally.LinkSkipped();
        return true;
    }

    private async Task EnqueueDirectoryItemsAsync(
        IList<string> filesInDirectory,
        string rootPath,
        FailingFiles failing,
        ChannelWriter<IngestionItem> writer,
        CrawlTally tally,
        CancellationToken ct)
    {
        var linkResult = livePhotoLinker.Link(filesInDirectory, rootPath);

        foreach (var item in linkResult.Items)
            await EnqueueAsync(item, failing, writer, tally, ct);

        foreach (var filePath in filesInDirectory)
        {
            if (linkResult.ClaimedPaths.Contains(filePath))
                continue;

            var mediaType = MediaTypeDetector.Detect(filePath);
            if (mediaType is null)
            {
                Log.UnsupportedFile(logger, filePath);
                tally.UnsupportedSkipped();
                continue;
            }

            var item = new SingleFileItem(filePath, fileSystem.Path.GetRelativePath(rootPath, filePath), mediaType.Value);
            await EnqueueAsync(item, failing, writer, tally, ct);
        }
    }

    private async Task EnqueueAsync(
        IngestionItem item, FailingFiles failing, ChannelWriter<IngestionItem> writer, CrawlTally tally, CancellationToken ct)
    {
        if (failing.Skipping(item) is { } failure)
        {
            Log.SkippingFailing(
                logger, item.AbsolutePath, failure.Failures, failure.FirstFailedAt, failure.Reason, failure.RetryAfter);
            tally.SkippedFailing(item);
            return;
        }

        tally.Found(item);
        await writer.WriteAsync(item, ct);
    }

    private async Task ConsumeAsync(
        UserStorageConfig config,
        ChannelReader<IngestionItem> reader,
        int workerIndex,
        FailingFiles failing,
        CrawlTally tally,
        ConcurrentQueue<IngestionItem>? retryLater,
        CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct))
        {
            var filename = fileSystem.Path.GetFileName(item.AbsolutePath);
            using var logScope = logger.BeginScope(
                "W{WorkerIndex} {IngestionFile}", workerIndex, filename);
            // Scoped services (e.g. DbContext) must not be shared across concurrent files.
            using var diScope = scopeFactory.CreateScope();
            var runner = diScope.ServiceProvider.GetRequiredService<IIngestionPipelineRunner>();
            var fingerprint = failing.Fingerprint(item);
            try
            {
                var context = new IngestionContext { Item = item, Config = config, AttemptId = guidFactory.NewGuid() };
                await runner.RunAsync(context, ct);
                tally.Processed(context);
            }
            catch (Exception ex) when (IngestionCancellation.IsRequested(ex, ct))
            {
                // A shutdown or a cancel is not an item failure: stop promptly instead of draining the queue.
                throw;
            }
            catch (Exception ex) when (BrokenFile.Rejected(ex))
            {
                Log.ItemBroken(logger, item.AbsolutePath, ex.Message);
                tally.Failed(FailedItem.Of(item, fingerprint, ex));
            }
            catch (Exception ex) when (retryLater is not null)
            {
                Log.ItemRetrying(logger, item.AbsolutePath, ex.Message);
                retryLater.Enqueue(item);
            }
            catch (Exception ex)
            {
                Log.ItemFailed(logger, item.AbsolutePath, ex);
                tally.Failed(FailedItem.Of(item, fingerprint, ex));
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping unsupported file type: {Path}.")]
        public static partial void UnsupportedFile(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to ingest {Path}.")]
        public static partial void ItemFailed(ILogger logger, string path, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to ingest {Path}: {Reason}")]
        public static partial void ItemBroken(ILogger logger, string path, string reason);

        [LoggerMessage(Level = LogLevel.Information, Message = "Failed to ingest {Path}, trying again after the walk: {Reason}")]
        public static partial void ItemRetrying(ILogger logger, string path, string reason);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Skipping {Path}: it failed on {Failures} crawls in a row since {FirstFailedAt}, last with {Reason}. "
                + "It is tried again from {RetryAfter}, or as soon as it changes.")]
        public static partial void SkippingFailing(
            ILogger logger, string path, int failures, Instant firstFailedAt, string reason, Instant retryAfter);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping link {Path}: a crawl never follows links.")]
        public static partial void LinkSkipped(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unreadable folder {Path}.")]
        public static partial void DirectoryUnreadable(ILogger logger, string path, Exception ex);
    }
}
