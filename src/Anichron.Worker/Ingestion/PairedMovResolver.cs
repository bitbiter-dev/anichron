using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;

namespace Anichron.Worker.Ingestion;

internal sealed partial class PairedMovResolver(
    IMediaAssetRepository repository,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<PairedMovResolver> logger)
{
    /// <summary>
    /// Points <paramref name="photo"/> at the item's MOV row, stored or added now; every other photo still pointing at a
    /// stored one lets go of it.
    /// </summary>
    /// <param name="context">The classified Live Photo item.</param>
    /// <param name="photo">The row that becomes the video's photo, dated already.</param>
    /// <param name="ct">Cancels the lookup of the other photos.</param>
    /// <returns>The MOV row.</returns>
    public async Task<MediaAsset> PairAsync(IngestionContext context, MediaAsset photo, CancellationToken ct)
    {
        var video = context.Classification!.Secondary!;
        var stored = video.Row;
        if (stored is not null)
            await TakeFromOtherPhotosAsync(photo, stored, ct);

        var mov = stored ?? Add(context);
        if (stored is null || video.TakesContent)
            DateFromPhoto(mov, photo);

        photo.PairedAssetId = mov.Id;
        return mov;
    }

    /// <summary>
    /// The one rule that dates a Live Photo's video: it takes its photo's whole capture date, and is undated when the photo
    /// is or when there is no photo row.
    /// </summary>
    /// <param name="video">The video row.</param>
    /// <param name="photo">Its photo's row as it stands in this item.</param>
    public static void DateFromPhoto(MediaAsset video, MediaAsset? photo) => video.Captured = photo?.Captured;

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

    private MediaAsset Add(IngestionContext context)
    {
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
