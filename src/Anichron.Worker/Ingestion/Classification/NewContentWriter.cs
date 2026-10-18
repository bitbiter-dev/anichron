using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed class NewContentWriter(
    IMediaAssetRepository repository,
    IngestionSaver saver,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter,
    ILogger<NewContentWriter> logger)
    : ProcessedContentWriter(saver, fileSystem, clock, pairedMovResolver, pairedMovProxyWriter, logger)
{
    public override FileOutcome Outcome => FileOutcome.New;

    protected override Task<MediaAsset> WriteAssetAsync(IngestionContext context, CancellationToken ct)
    {
        var relativePath = context.Item.RelativePath;
        var metadata = new Metadata();
        var asset = new MediaAsset
        {
            Id = context.AssetId,
            StorageConfigId = context.Config.Id,
            FilePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            IsSoftDeleted = false,
            Metadata = metadata,
        };
        DescribeContent(asset, metadata, context);

        repository.Add(asset);
        repository.AddProxyFiles(context.ProxyFilesOf(asset.Id));
        return Task.FromResult(asset);
    }
}
