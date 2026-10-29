using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;

namespace Anichron.Worker.Ingestion.Classification;

internal interface IOutcomeWriter
{
    IReadOnlyCollection<FileOutcome> Outcomes { get; }

    Task<MediaAsset> WriteAsync(IngestionContext context, CancellationToken ct);
}
