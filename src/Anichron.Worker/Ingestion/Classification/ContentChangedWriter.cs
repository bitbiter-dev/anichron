using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class ContentChangedWriter(
    IMediaAssetRepository repository,
    IBurstRepository bursts,
    IngestionSaver saver,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<ContentChangedWriter> logger)
    : ProcessedContentWriter(saver, fileSystem, clock, pairedMovResolver, pairedMovProxyWriter, logger)
{
    public override IReadOnlyCollection<FileOutcome> Outcomes => [FileOutcome.ContentChanged, FileOutcome.AutoRestoredContentChanged];

    protected override async Task<MediaAsset> WriteAssetAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = context.Classification!.Primary.Stored!;
        Log.ContentChanged(logger, asset.Id, context.Item.RelativePath, asset.ContentHash, context.ContentHash!);

        await repository.LoadMetadataAsync(asset, ct);
        DescribeContent(asset, asset.Metadata ??= new Metadata { AssetId = asset.Id }, context);
        ProxyFileRows.Replace(repository, asset, context.ProxyFilesOf(asset.Id));
        asset.BurstId = null;
        await bursts.RemoveCheckAsync(asset.Id, ct);
        if (asset.IsSoftDeleted)
        {
            asset.IsSoftDeleted = false;
            context.CountAutoRestored();
            Log.AutoRestored(logger, asset.Id, context.Item.RelativePath);
        }

        return asset;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Asset {AssetId} at {RelativePath} changed content from {OldHash} to {NewHash}, updating it in place.")]
        public static partial void ContentChanged(ILogger logger, Guid assetId, string relativePath, string oldHash, string newHash);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Auto-restored asset {AssetId} at {RelativePath} with its new content.")]
        public static partial void AutoRestored(ILogger logger, Guid assetId, string relativePath);
    }
}
