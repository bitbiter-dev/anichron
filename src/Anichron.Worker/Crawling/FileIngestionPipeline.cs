using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;
using System.Threading.Channels;

namespace Anichron.Worker.Crawling;

internal interface IFileIngestionPipeline
{
    Task<CrawlResult> RunAsync(UserStorageConfig config, IWorkProgress progress, CancellationToken ct);
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

    public async Task<CrawlResult> RunAsync(UserStorageConfig config, IWorkProgress progress, CancellationToken ct)
    {
        var tally = new CrawlTally(progress);
        var channel = Channel.CreateBounded<IngestionItem>(
            // Stryker disable once Arithmetic,Initializer
            new BoundedChannelOptions(settings.MaxConcurrentFiles * 2)
            {
                // Stryker disable once Boolean
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        var producer = ProduceAsync(config, channel.Writer, tally, ct);
        var consumers = Enumerable
            .Range(0, settings.MaxConcurrentFiles)
            .Select(workerIndex => ConsumeAsync(config, channel.Reader, workerIndex, tally, ct))
            .ToArray();

        await Task.WhenAll([producer, .. consumers]);
        return tally.Result;
    }

    private async Task ProduceAsync(
        UserStorageConfig config,
        ChannelWriter<IngestionItem> writer,
        CrawlTally tally,
        CancellationToken ct)
    {
        try
        {
            var pending = new Stack<string>([config.RootPath]);
            while (pending.TryPop(out var directory))
            {
                var (files, subdirectories) = ListDirectory(directory, config.RootPath);
                await EnqueueDirectoryItemsAsync(files, config.RootPath, writer, tally, ct);
                foreach (var subdirectory in subdirectories)
                    pending.Push(subdirectory);
            }

            tally.Walked();
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private (List<string> Files, List<string> Subdirectories) ListDirectory(string directory, string rootPath)
    {
        try
        {
            return (
                [.. fileSystem.Directory.EnumerateFiles(directory)
                    .Where(file => !CrawlExclusions.IsExcludedFile(fileSystem.Path.GetFileName(file)))],
                [.. fileSystem.Directory.EnumerateDirectories(directory)
                    .Where(subdirectory => !exclusions.IsExcludedDirectory(fileSystem.Path.GetFileName(subdirectory)))]);
        }
        catch (Exception ex) when (directory != rootPath && ex is UnauthorizedAccessException or IOException)
        {
            Log.DirectoryUnreadable(logger, directory, ex);
            return ([], []);
        }
    }

    private async Task EnqueueDirectoryItemsAsync(
        IList<string> filesInDirectory,
        string rootPath,
        ChannelWriter<IngestionItem> writer,
        CrawlTally tally,
        CancellationToken ct)
    {
        var linkResult = livePhotoLinker.Link(filesInDirectory, rootPath);

        foreach (var item in linkResult.Items)
        {
            tally.Found();
            await writer.WriteAsync(item, ct);
        }

        foreach (var filePath in filesInDirectory)
        {
            if (linkResult.ClaimedPaths.Contains(filePath))
                continue;

            var mediaType = MediaTypeDetector.Detect(filePath);
            if (mediaType is null)
            {
                Log.UnsupportedFile(logger, filePath);
                continue;
            }

            var relativePath = fileSystem.Path.GetRelativePath(rootPath, filePath);
            tally.Found();
            await writer.WriteAsync(new SingleFileItem(filePath, relativePath, mediaType.Value), ct);
        }
    }

    private async Task ConsumeAsync(
        UserStorageConfig config,
        ChannelReader<IngestionItem> reader,
        int workerIndex,
        CrawlTally tally,
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
            try
            {
                var context = new IngestionContext { Item = item, Config = config, AttemptId = guidFactory.NewGuid() };
                await runner.RunAsync(context, ct);
                tally.Processed();
            }
            catch (Exception ex) when (IngestionShutdown.IsInProgress(ex, ct))
            {
                // A shutdown is not an item failure: stop promptly instead of draining the queue.
                throw;
            }
            catch (Exception ex)
            {
                Log.ItemFailed(logger, item.AbsolutePath, ex);
                tally.Failed();
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unsupported file type: {Path}.")]
        public static partial void UnsupportedFile(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to ingest {Path}.")]
        public static partial void ItemFailed(ILogger logger, string path, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unreadable folder {Path}.")]
        public static partial void DirectoryUnreadable(ILogger logger, string path, Exception ex);
    }
}
