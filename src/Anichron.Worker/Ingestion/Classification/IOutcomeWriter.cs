using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Classification;

internal interface IOutcomeWriter
{
    FileOutcome Outcome { get; }

    Task<MediaAsset?> TryWriteAsync(IngestionContext context, CancellationToken ct);
}
