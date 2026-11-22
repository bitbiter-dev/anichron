using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Ingestion;
using Anichron.Worker.Ingestion.Proxy;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;

namespace Anichron.Worker.CompactPreviews;

public sealed record CompactPreviewResult
{
    public int Converted { get; init; }
    public int Failed { get; init; }
    public int Remaining { get; init; }
}

internal sealed partial class CompactPreviewHandler(
    ActiveStorageConfigs activeConfigs,
    IMediaAssetRepository assets,
    IUnitOfWork unitOfWork,
    IWorkQueue queue,
    IProxyDirectoryStrategy directories,
    ProxyStagingWriter writer,
    ICompactPreviewEncoder encoder,
    IFileSystem fileSystem,
    IGuidFactory guids,
    IOptions<WorkerSettings> options,
    ILogger<CompactPreviewHandler> logger) : IWorkHandler<CompactPreviewPayload>
{
    internal const string FileName = "compact-preview.jxl";

    public async Task<object?> RunAsync(CompactPreviewPayload payload, WorkRun run, CancellationToken ct)
    {
        if (await activeConfigs.FindAsync(CompactPreviewPayload.Kind, payload.StorageConfigId, ct) is not { } config)
            return StorageConfigSkipped.MissingOrInactive;

        var chunk = await assets.GetWithoutCompactPreviewAsync(config.Id, payload.After, options.Value.CompactPreviewChunkSize, ct);
        var attemptId = guids.NewGuid();
        var (converted, failed) = (0, 0);
        foreach (var asset in chunk)
        {
            if (await ConvertAsync(asset, attemptId, ct))
                converted++;
            else
                failed++;
            run.Progress.Report(converted + failed, chunk.Count, "Making compact previews");
        }

        var after = chunk.Count > 0 ? chunk[^1].Id : payload.After;
        var remaining = await assets.CountWithoutCompactPreviewAsync(config.Id, after, ct);
        if (remaining > 0)
            await queue.EnqueueAsync(new CompactPreviewPayload(config.Id, after), null, ct);

        Log.Finished(logger, config.Id, converted, failed, remaining);
        return new CompactPreviewResult { Converted = converted, Failed = failed, Remaining = remaining };
    }

    private async Task<bool> ConvertAsync(MediaAsset asset, Guid attemptId, CancellationToken ct)
    {
        ProxyFile proxy;
        try
        {
            var preview = asset.ProxyFiles.Single(p => p.ProxyType == ProxyType.FullPreview);
            var source = StoragePath.ResolveProxy(preview.ProxyPath)
                ?? throw new InvalidOperationException($"Proxy path {preview.ProxyPath} resolves outside the proxy root.");
            var bytes = await fileSystem.File.ReadAllBytesAsync(source, ct);
            proxy = await writer.PlaceAsync(
                asset.Id,
                $"{directories.GetDirectory(asset.StorageConfigId, asset.ContentHash)}/{FileName}",
                ProxyType.CompactPreview,
                attemptId,
                (path, token) => fileSystem.File.WriteAllBytesAsync(path, encoder.Encode(bytes), token),
                ct);
        }
        catch (Exception ex) when (!IngestionCancellation.IsRequested(ex, ct))
        {
            Log.Failed(logger, asset.Id, ex);
            return false;
        }

        assets.AddProxyFiles([proxy]);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Compact previews for storage config {Id}: {Converted} made, {Failed} failed, {Remaining} remaining.")]
        public static partial void Finished(ILogger logger, Guid id, int converted, int failed, int remaining);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Making the compact preview of asset {AssetId} failed.")]
        public static partial void Failed(ILogger logger, Guid assetId, Exception ex);
    }
}
