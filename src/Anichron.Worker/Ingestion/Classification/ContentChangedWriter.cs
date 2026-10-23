using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class ContentChangedWriter(
    IMediaAssetRepository repository,
    IngestionSaver saver,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<ContentChangedWriter> logger)
    : ProcessedContentWriter(saver, fileSystem, clock, pairedMovResolver, pairedMovProxyWriter, logger)
{
    public override FileOutcome Outcome => FileOutcome.ContentChanged;

    protected override async Task<MediaAsset> WriteAssetAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = context.Classification!.Primary.Stored!;
        Log.ContentChanged(logger, asset.Id, context.Item.RelativePath, asset.ContentHash, context.ContentHash!);

        await repository.LoadMetadataAsync(asset, ct);
        DescribeContent(asset, asset.Metadata ??= new Metadata { AssetId = asset.Id }, context);
        ReplaceProxyFiles(asset, context.ProxyFilesOf(asset.Id));
        asset.BurstId = null;
        await repository.RemoveBurstCheckAsync(asset.Id, ct);
        return asset;
    }

    private void ReplaceProxyFiles(MediaAsset asset, IReadOnlyList<ProxyFile> generated)
    {
        var stale = asset.ProxyFiles.Where(old => generated.All(proxy => proxy.ProxyType != old.ProxyType));
        foreach (var proxy in stale.ToList())
            repository.RemoveProxyFile(proxy);

        var added = new List<ProxyFile>();
        foreach (var proxy in generated)
        {
            if (asset.ProxyFiles.FirstOrDefault(old => old.ProxyType == proxy.ProxyType) is not { } existing)
            {
                added.Add(proxy);
                continue;
            }

            existing.ProxyPath = proxy.ProxyPath;
            existing.SizeBytes = proxy.SizeBytes;
            existing.CreatedAt = proxy.CreatedAt;
        }

        repository.AddProxyFiles(added);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} at {RelativePath} changed content from {OldHash} to {NewHash}, updating it in place.")]
        public static partial void ContentChanged(ILogger logger, Guid assetId, string relativePath, string oldHash, string newHash);
    }
}
