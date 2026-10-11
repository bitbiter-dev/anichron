using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion;

internal sealed class PairedMovResolver(
    IMediaAssetRepository repository,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guidFactory)
{
    public async Task<Guid> ResolveAsync(
        SecondaryFileDescriptor mov,
        string contentHash,
        Guid storageConfigId,
        LocalDateTime? dateCaptured,
        CancellationToken ct)
    {
        var existing = await repository.FindByHashAsync(contentHash, storageConfigId, ct);
        if (existing is not null)
            return existing.Id;

        var date = dateCaptured
            ?? LocalDateTime.FromDateTime(fileSystem.FileInfo.New(mov.AbsolutePath).LastWriteTime);

        var asset = new MediaAsset
        {
            Id = guidFactory.NewGuid(),
            StorageConfigId = storageConfigId,
            FilePath = mov.RelativePath,
            FileName = Path.GetFileName(mov.RelativePath),
            ContentHash = contentHash,
            DateCaptured = date,
            Month = date.Month,
            Day = date.Day,
            Year = date.Year,
            MediaType = mov.MediaType,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
        };
        repository.Add(asset);
        return asset.Id;
    }
}
