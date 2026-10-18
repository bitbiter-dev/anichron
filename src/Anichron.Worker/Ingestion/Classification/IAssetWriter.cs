using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Classification;

internal interface IAssetWriter
{
    FileOutcome Writes { get; }

    Task<MediaAsset?> TryWriteAsync(IngestionContext context, CancellationToken ct);
}
