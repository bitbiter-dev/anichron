using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Ingestion;

internal sealed partial class PairedMovResolver(
    IMediaAssetRepository repository,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<PairedMovResolver> logger)
{
    /// <summary>
    /// Points <paramref name="photo"/> at the item's MOV row, stored or added now; every other photo still pointing at a
    /// stored one lets go of it.
    /// </summary>
    /// <param name="context">The classified Live Photo item.</param>
    /// <param name="photo">The row that becomes the video's photo.</param>
    /// <param name="dateCaptured">The capture date an added MOV row takes, before its last-write time.</param>
    /// <param name="ct">Cancels the lookup of the other photos.</param>
    /// <returns>The MOV row.</returns>
    public async Task<MediaAsset> PairAsync(
        IngestionContext context, MediaAsset photo, LocalDateTime? dateCaptured, CancellationToken ct)
    {
        var stored = context.Classification!.Secondary!.Row;
        if (stored is not null)
            await TakeFromOtherPhotosAsync(photo, stored, ct);

        var mov = stored ?? Add(context, dateCaptured);
        photo.PairedAssetId = mov.Id;
        return mov;
    }

    private async Task TakeFromOtherPhotosAsync(MediaAsset photo, MediaAsset video, CancellationToken ct)
    {
        foreach (var other in await repository.FindPhotosOfAsync(video.Id, ct))
        {
            if (other == photo || other.PairedAssetId != video.Id)
                continue;

            other.PairedAssetId = null;
            if (!other.IsSoftDeleted)
                Log.NowAStillPhoto(logger, other.Id, video.Id, photo.Id);
        }
    }

    private MediaAsset Add(IngestionContext context, LocalDateTime? dateCaptured)
    {
        var mov = context.Item.SecondaryFile!;
        var asset = new MediaAsset
        {
            Id = guidFactory.NewGuid(),
            StorageConfigId = context.Config.Id,
            FilePath = mov.RelativePath,
            FileName = Path.GetFileName(mov.RelativePath),
            ContentHash = context.SecondaryHash!,
            DateCaptured = CaptureDate.FromExifOrLastWrite(dateCaptured, fileSystem, mov.AbsolutePath),
            MediaType = mov.MediaType,
            IsSoftDeleted = false,
            LastSeenOnNas = clock.GetCurrentInstant(),
        };
        repository.Add(asset);
        return asset;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Asset {PhotoId} is now a still photo: its video, asset {VideoId}, is now the video of asset {NewPhotoId}.")]
        public static partial void NowAStillPhoto(ILogger logger, Guid photoId, Guid videoId, Guid newPhotoId);
    }
}
