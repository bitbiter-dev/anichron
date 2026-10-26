using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed partial class ContentChangedWriter(
    StoredContent storedContent,
    IngestionSaver saver,
    IFileSystem fileSystem,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<ContentChangedWriter> logger)
    : ProcessedContentWriter(saver, fileSystem, pairedMovResolver, pairedMovProxyWriter, logger)
{
    public override IReadOnlyCollection<FileOutcome> Outcomes => [FileOutcome.ContentChanged, FileOutcome.AutoRestoredContentChanged];

    protected override async Task<MediaAsset> WriteAssetAsync(IngestionContext context, CancellationToken ct)
    {
        var asset = context.Classification!.Primary.Stored!;
        Log.ContentChanged(logger, asset.Id, context.Item.RelativePath, asset.ContentHash, context.ContentHash!);

        if (await storedContent.TakeAsync(context, asset, ContentOf(context, asset.Id), ct))
            Log.AutoRestored(logger, asset.Id, context.Item.RelativePath);

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
