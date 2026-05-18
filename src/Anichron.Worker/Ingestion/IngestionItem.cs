using Anichron.Core.Domain;

namespace Anichron.Worker.Ingestion;

internal abstract record IngestionItem(string AbsolutePath, string RelativePath);

internal sealed record SingleFileItem(
    string AbsolutePath,
    string RelativePath,
    MediaType MediaType) : IngestionItem(AbsolutePath, RelativePath);

internal sealed record LivePhotoPairItem(
    string HeicAbsolutePath,
    string HeicRelativePath,
    string MovAbsolutePath,
    string MovRelativePath) : IngestionItem(HeicAbsolutePath, HeicRelativePath);
