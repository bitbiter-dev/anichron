using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion;

internal sealed class PairedMovResolver(
    IMediaAssetRepository repository,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guidFactory)
{
    public MediaAsset Resolve(IngestionContext context, LocalDateTime? dateCaptured)
    {
        var stored = context.Classification!.Secondary!.Stored;
        if (stored is not null)
            return stored;

        var mov = context.Item.SecondaryFile!;
        var asset = new MediaAsset
        {
            Id = guidFactory.NewGuid(),
            StorageConfigId = context.Config.Id,
            FilePath = mov.RelativePath,
            FileName = Path.GetFileName(mov.RelativePath),
            ContentHash = context.SecondaryHash!,
            MediaType = mov.MediaType,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
        };
        asset.SetCaptureDate(CaptureDate.Resolve(dateCaptured, fileSystem, mov.AbsolutePath));
        repository.Add(asset);
        return asset;
    }
}
