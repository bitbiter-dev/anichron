using Anichron.Core.Domain;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Settings;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;
using System.Threading.Channels;

namespace Anichron.Worker.Crawling;

internal interface IFileIngestionPipeline
{
    Task RunAsync(UserStorageConfig config, CancellationToken ct);
}

internal sealed partial class FileIngestionPipeline(
    IEnumerable<IIngestionMiddleware> middlewares,
    IFileSystem fileSystem,
    ILivePhotoLinker livePhotoLinker,
    IOptions<WorkerSettings> workerOptions,
    ILogger<FileIngestionPipeline> logger) : IFileIngestionPipeline
{
    private readonly IngestionDelegate _pipeline = IngestionPipelineBuilder.Build([.. middlewares]);
    private readonly WorkerSettings _settings = workerOptions.Value;

    public async Task RunAsync(UserStorageConfig config, CancellationToken ct)
    {
        var channel = Channel.CreateBounded<IngestionItem>(
            new BoundedChannelOptions(_settings.MaxConcurrentFiles * 2)
            {
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        var producer = ProduceAsync(config, channel.Writer, ct);
        var consumers = Enumerable
            .Range(0, _settings.MaxConcurrentFiles)
            .Select(_ => ConsumeAsync(config, channel.Reader, ct))
            .ToArray();

        await Task.WhenAll([producer, .. consumers]);
    }

    private async Task ProduceAsync(
        UserStorageConfig config,
        ChannelWriter<IngestionItem> writer,
        CancellationToken ct)
    {
        try
        {
            var filesByDirectory = fileSystem.Directory
                .EnumerateFiles(config.RootPath, "*", SearchOption.AllDirectories)
                .GroupBy(path => fileSystem.Path.GetDirectoryName(path) ?? string.Empty);

            foreach (var directoryGroup in filesByDirectory)
                await EnqueueDirectoryItemsAsync([.. directoryGroup], config.RootPath, writer, ct);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task EnqueueDirectoryItemsAsync(
        IList<string> filesInDirectory,
        string rootPath,
        ChannelWriter<IngestionItem> writer,
        CancellationToken ct)
    {
        var linkResult = livePhotoLinker.Link(filesInDirectory, rootPath);

        foreach (var item in linkResult.Items)
            await writer.WriteAsync(item, ct);

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
            await writer.WriteAsync(new SingleFileItem(filePath, relativePath, mediaType.Value), ct);
        }
    }

    private async Task ConsumeAsync(
        UserStorageConfig config,
        ChannelReader<IngestionItem> reader,
        CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct))
        {
            try
            {
                var context = new IngestionContext { Item = item, Config = config };
                await _pipeline(context, ct);
            }
            catch (PipelineConfigurationException ex)
            {
                Log.PipelineMisconfigured(logger, ex);
                throw;
            }
            catch (Exception ex)
            {
                Log.ItemFailed(logger, item.AbsolutePath, ex);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping unsupported file type: {Path}.")]
        public static partial void UnsupportedFile(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Critical, Message = "Ingestion pipeline is misconfigured and cannot continue.")]
        public static partial void PipelineMisconfigured(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to ingest {Path}.")]
        public static partial void ItemFailed(ILogger logger, string path, Exception ex);
    }
}
