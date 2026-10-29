using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Anichron.Worker.Ingestion.Proxy;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion.Classification;

internal sealed class NewContentWriter(
    IMediaAssetRepository repository,
    IFileSystem fileSystem,
    IClock clock,
    PairedMovResolver pairedMovResolver,
    PairedMovProxyWriter pairedMovProxyWriter)
    : ProcessedContentWriter(fileSystem, pairedMovResolver, pairedMovProxyWriter)
{
    public override IReadOnlyCollection<FileOutcome> Outcomes => [FileOutcome.New];

    protected override Task<MediaAsset> WriteAssetAsync(IngestionContext context, CancellationToken ct)
    {
        var relativePath = context.Item.RelativePath;
        var content = ContentOf(context, context.AssetId);
        var asset = new MediaAsset
        {
            Id = context.AssetId,
            StorageConfigId = context.Config.Id,
            FilePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
            Metadata = content.Description,
        };
        content.Description.AssetId = asset.Id;
        content.ApplyTo(asset);

        repository.Add(asset);
        repository.AddProxyFiles(content.Proxies);
        return Task.FromResult(asset);
    }
}
