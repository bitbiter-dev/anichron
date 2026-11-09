using Anichron.Core;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;

namespace Anichron.Worker.Ingestion;

internal sealed partial class PairedMovResolver(
    IMediaAssetRepository repository,
    StoredContent storedContent,
    IClock clock,
    IGuidFactory guidFactory,
    ILogger<PairedMovResolver> logger)
{
    /// <summary>
    /// Points <paramref name="photo"/> at the item's MOV row, stored or added now, and marks that row a Live Photo's video;
    /// every other photo still pointing at a stored one lets go of it. The video <paramref name="photo"/> lets go of is
    /// soft-deleted, unless the item's video half retires it.
    /// </summary>
    /// <param name="context">The classified Live Photo item, which stages the soft-delete count.</param>
    /// <param name="photo">The row that becomes the video's photo, dated already.</param>
    /// <param name="ct">Cancels the lookups of the other photos and of the previous video.</param>
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

        await LetGoOfPreviousAsync(context, photo, mov, video.Retired, ct);
        photo.PairedAssetId = mov.Id;
        mov.IsLivePhotoVideo = true;
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

    private async Task LetGoOfPreviousAsync(
        IngestionContext context, MediaAsset photo, MediaAsset mov, MediaAsset? retiredByTheItem, CancellationToken ct)
    {
        if (photo.PairedAssetId is not { } previousId || previousId == mov.Id || previousId == retiredByTheItem?.Id)
            return;
        if (await repository.FindAsync(previousId, ct) is not { IsSoftDeleted: false } previous)
            return;

        await storedContent.RetireAsync(context, previous, ct);
        Log.LetGo(logger, previous.Id, previous.FilePath, photo.Id, mov.Id);
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

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Soft-deleted asset {VideoId} at {FilePath}: its photo, asset {PhotoId}, now has another video, asset {NewVideoId}.")]
        public static partial void LetGo(ILogger logger, Guid videoId, string filePath, Guid photoId, Guid newVideoId);
    }
}
