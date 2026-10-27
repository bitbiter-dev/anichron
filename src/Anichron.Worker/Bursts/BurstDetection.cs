using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Settings;
using Anichron.Worker.Work;
using Microsoft.Extensions.Options;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Bursts;

public sealed record BurstDetectionResult
{
    public int Checked { get; init; }
    public int Created { get; init; }
    public int Merged { get; init; }
    public int Dissolved { get; init; }
    public int Matched { get; init; }
    public int NotMatched { get; init; }
}

internal sealed partial class BurstDetectionHandler(
    ActiveStorageConfigs activeConfigs,
    IBurstRepository bursts,
    IUnitOfWork unitOfWork,
    IOriginalPhotoReader reader,
    IThumbnailMatcher matcher,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guids,
    IOptions<WorkerSettings> options,
    ILogger<BurstDetectionHandler> logger) : IWorkHandler<BurstDetectionPayload>
{
    internal const int BatchSize = 100;

    public async Task<object?> RunAsync(BurstDetectionPayload payload, WorkRun run, CancellationToken ct)
    {
        if (await activeConfigs.FindAsync(BurstDetectionPayload.Kind, payload.StorageConfigId, ct) is not { } config)
            return StorageConfigSkipped.MissingOrInactive;

        if (!fileSystem.Directory.Exists(config.RootPath))
            throw new DirectoryNotFoundException($"The storage config's root {config.RootPath} is missing.");

        var tally = new Tally();
        await RepairAsync(config.Id, tally, ct);
        await CheckAsync(config, tally, run.Progress, ct);

        Log.Finished(logger, config.Id, tally.Checked, tally.Created, tally.Merged, tally.Dissolved, tally.Matched, tally.NotMatched);
        return tally.ToResult();
    }

    private async Task RepairAsync(Guid configId, Tally tally, CancellationToken ct)
    {
        foreach (var burst in await bursts.GetInConfigWithMembersAsync(configId, ct))
        {
            if (burst.Assets.Count(a => !a.IsSoftDeleted) < 2)
            {
                bursts.Remove(burst);
                tally.Dissolved++;
            }
            else
            {
                burst.PrimaryAssetId = BurstGrouping.Cover(burst.Assets).Id;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        tally.Dissolved += await bursts.DeleteMemberlessAsync(ct);
    }

    private async Task CheckAsync(UserStorageConfig config, Tally tally, IWorkProgress progress, CancellationToken ct)
    {
        var total = await bursts.CountUncheckedPhotosAsync(config.Id, ct);
        MediaAsset? last = null;
        while (await bursts.GetUncheckedPhotosAsync(config.Id, last, BatchSize, ct) is { Count: > 0 } batch)
        {
            foreach (var photo in batch)
            {
                await CheckAsync(config, photo, tally, ct);
                progress.Report(++tally.Checked, total, "Checking photos for bursts");
            }

            last = batch[^1];
            bursts.ForgetLoaded();
            matcher.Forget();
        }
    }

    private async Task CheckAsync(UserStorageConfig config, MediaAsset photo, Tally tally, CancellationToken ct)
    {
        var facts = reader.Read(fileSystem.Path.Combine(config.RootPath, photo.FilePath));
        var check = new BurstCheck
        {
            AssetId = photo.Id,
            CheckedAt = clock.GetCurrentInstant(),
            IsCandidate = facts.ExifCapturedAt is not null
                && photo.Metadata is { CameraMake: not null, CameraModel: not null }
                && photo.ProxyFiles.Any(p => p.ProxyType == ProxyType.Thumbnail),
            CameraBurstId = facts.CameraBurstId,
        };
        bursts.AddCheck(check);
        if (check.IsCandidate)
            await GroupAsync(config.Id, photo, check.CameraBurstId, tally, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task GroupAsync(Guid configId, MediaAsset photo, string? label, Tally tally, CancellationToken ct)
    {
        var labelled = label is null ? [] : await bursts.GetCandidatesLabelledAsync(configId, label, ct);
        var similar = await SimilarNeighboursAsync(configId, photo, labelled, tally, ct);
        if (labelled.Count + similar.Count == 0)
            return;

        var joined = await bursts.GetWithMembersAsync(
            [.. labelled.Concat(similar).Select(a => a.BurstId).OfType<Guid>().Distinct()], ct);
        if (BurstGrouping.Join(photo, labelled, similar, joined, options.Value.BurstSpanSeconds) is not { } join)
            return;

        var target = join.Target;
        if (target is null)
        {
            target = new Burst { Id = guids.NewGuid(), CreatedAt = clock.GetCurrentInstant() };
            bursts.Add(target);
            tally.Created++;
        }

        foreach (var merged in join.Merged)
        {
            bursts.Remove(merged);
            tally.Merged++;
        }

        foreach (var member in join.Members)
            member.BurstId = target.Id;
        target.PrimaryAssetId = join.Cover.Id;
    }

    private async Task<IReadOnlyList<MediaAsset>> SimilarNeighboursAsync(
        Guid configId, MediaAsset photo, IReadOnlyCollection<MediaAsset> labelled, Tally tally, CancellationToken ct)
    {
        var labelledBursts = labelled.Select(a => a.BurstId).OfType<Guid>().ToHashSet();
        var gap = options.Value.BurstGapSeconds;
        var neighbours = await bursts.GetCandidatesNearAsync(
            configId,
            photo.Metadata!.CameraMake!,
            photo.Metadata.CameraModel!,
            photo.DateCaptured.PlusSeconds(-gap),
            photo.DateCaptured.PlusSeconds(gap),
            ct);

        var similar = new List<MediaAsset>();
        foreach (var neighbour in neighbours.Where(n => !labelled.Contains(n) && !(n.BurstId is { } id && labelledBursts.Contains(id))))
        {
            var score = await matcher.ScoreAsync(Thumbnail(photo), Thumbnail(neighbour), ct);
            Log.Compared(logger, photo.Id, neighbour.Id, score);
            if (score >= options.Value.BurstSimilarityThreshold)
            {
                similar.Add(neighbour);
                tally.Matched++;
            }
            else
            {
                tally.NotMatched++;
            }
        }

        return similar;
    }

    private static string Thumbnail(MediaAsset asset)
        // Stryker disable once Linq
        => asset.ProxyFiles.First(p => p.ProxyType == ProxyType.Thumbnail).ProxyPath;

    private sealed class Tally
    {
        public int Checked { get; set; }
        public int Created { get; set; }
        public int Merged { get; set; }
        public int Dissolved { get; set; }
        public int Matched { get; set; }
        public int NotMatched { get; set; }

        public BurstDetectionResult ToResult() => new()
        {
            Checked = Checked,
            Created = Created,
            Merged = Merged,
            Dissolved = Dissolved,
            Matched = Matched,
            NotMatched = NotMatched,
        };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Burst detection for storage config {Id} checked {Checked} photo(s): {Created} burst(s) created, {Merged} merged, {Dissolved} dissolved; {Matched} compared pair(s) matched, {NotMatched} did not.")]
        public static partial void Finished(ILogger logger, Guid id, int @checked, int created, int merged, int dissolved, int matched, int notMatched);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Burst detection compared photo {Photo} with {Neighbour}: score {Score:F3}.")]
        public static partial void Compared(ILogger logger, Guid photo, Guid neighbour, double score);
    }
}
