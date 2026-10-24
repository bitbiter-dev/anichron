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
    public int Checked { get; set; }
    public int Created { get; set; }
    public int Merged { get; set; }
    public int Dissolved { get; set; }
    public int Matched { get; set; }
    public int NotMatched { get; set; }
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

    public async Task<object?> RunAsync(BurstDetectionPayload payload, IWorkProgress progress, CancellationToken ct)
    {
        if (await activeConfigs.FindAsync(BurstDetectionPayload.Kind, payload.StorageConfigId, ct) is not { } config)
            return StorageConfigSkipped.MissingOrInactive;

        if (!fileSystem.Directory.Exists(config.RootPath))
            throw new DirectoryNotFoundException($"The storage config's root {config.RootPath} is missing.");

        var result = new BurstDetectionResult();
        await RepairAsync(config.Id, result, ct);
        await CheckAsync(config, result, progress, ct);

        Log.Finished(logger, config.Id, result.Checked, result.Created, result.Merged, result.Dissolved, result.Matched, result.NotMatched);
        return result;
    }

    private async Task RepairAsync(Guid configId, BurstDetectionResult result, CancellationToken ct)
    {
        foreach (var burst in await bursts.GetInConfigWithMembersAsync(configId, ct))
        {
            if (burst.Assets.Count(a => !a.IsSoftDeleted) < 2)
            {
                bursts.Remove(burst);
                result.Dissolved++;
            }
            else
            {
                burst.PrimaryAssetId = Earliest(burst.Assets).Id;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        result.Dissolved += await bursts.DeleteMemberlessAsync(ct);
    }

    private async Task CheckAsync(UserStorageConfig config, BurstDetectionResult result, IWorkProgress progress, CancellationToken ct)
    {
        var total = await bursts.CountUncheckedPhotosAsync(config.Id, ct);
        while (await bursts.GetUncheckedPhotosAsync(config.Id, BatchSize, ct) is { Count: > 0 } batch)
        {
            foreach (var photo in batch)
            {
                await CheckAsync(config, photo, result, ct);
                progress.Report(++result.Checked, total, "Checking photos for bursts");
            }

            bursts.ForgetLoaded();
            matcher.Forget();
        }
    }

    private async Task CheckAsync(UserStorageConfig config, MediaAsset photo, BurstDetectionResult result, CancellationToken ct)
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
            await GroupAsync(config.Id, photo, check.CameraBurstId, result, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task GroupAsync(Guid configId, MediaAsset photo, string? label, BurstDetectionResult result, CancellationToken ct)
    {
        var labelled = label is null ? [] : await bursts.GetCandidatesLabelledAsync(configId, label, ct);
        var similar = await SimilarNeighboursAsync(configId, photo, result, ct);
        if (labelled.Count + similar.Count == 0)
            return;

        var joined = await bursts.GetWithMembersAsync(
            [.. labelled.Concat(similar).Select(a => a.BurstId).OfType<Guid>().Distinct()], ct);
        IEnumerable<MediaAsset> GroupOf(MediaAsset asset)
            => joined.FirstOrDefault(b => b.Id == asset.BurstId)?.Assets ?? [asset];

        HashSet<MediaAsset> members = [photo, .. labelled.SelectMany(GroupOf)];
        foreach (var neighbour in similar)
        {
            HashSet<MediaAsset> grown = [.. members, .. GroupOf(neighbour)];
            if (WithinSpan(grown))
                members = grown;
        }

        if (members.Count == 1)
            return;

        var included = joined.Where(b => members.Any(m => m.BurstId == b.Id)).ToList();
        if (included is not [var target, ..])
        {
            target = new Burst { Id = guids.NewGuid(), CreatedAt = clock.GetCurrentInstant() };
            bursts.Add(target);
            result.Created++;
        }

        foreach (var merged in included.Skip(1))
        {
            bursts.Remove(merged);
            result.Merged++;
        }

        foreach (var member in members)
            member.BurstId = target.Id;
        target.PrimaryAssetId = Earliest(members).Id;
    }

    private async Task<IReadOnlyList<MediaAsset>> SimilarNeighboursAsync(
        Guid configId, MediaAsset photo, BurstDetectionResult result, CancellationToken ct)
    {
        var gap = options.Value.BurstGapSeconds;
        var neighbours = await bursts.GetCandidatesNearAsync(
            configId,
            photo.Metadata!.CameraMake!,
            photo.Metadata.CameraModel!,
            photo.DateCaptured.PlusSeconds(-gap),
            photo.DateCaptured.PlusSeconds(gap),
            ct);

        var similar = new List<MediaAsset>();
        foreach (var neighbour in neighbours)
        {
            var score = await matcher.ScoreAsync(Thumbnail(photo), Thumbnail(neighbour), ct);
            Log.Compared(logger, photo.Id, neighbour.Id, score);
            if (score >= options.Value.BurstSimilarityThreshold)
            {
                similar.Add(neighbour);
                result.Matched++;
            }
            else
            {
                result.NotMatched++;
            }
        }

        return similar;
    }

    private bool WithinSpan(IEnumerable<MediaAsset> members)
    {
        var captured = members.Where(a => !a.IsSoftDeleted).Select(a => a.DateCaptured).ToList();
        return captured.Max() <= captured.Min().PlusSeconds(options.Value.BurstSpanSeconds);
    }

    private static string Thumbnail(MediaAsset asset)
        // Stryker disable once Linq
        => asset.ProxyFiles.First(p => p.ProxyType == ProxyType.Thumbnail).ProxyPath;

    private static MediaAsset Earliest(IEnumerable<MediaAsset> members)
        => members.Where(a => !a.IsSoftDeleted).MinBy(a => (a.DateCaptured, a.Id))!;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Burst detection for storage config {Id} checked {Checked} photo(s): {Created} burst(s) created, {Merged} merged, {Dissolved} dissolved; {Matched} compared pair(s) matched, {NotMatched} did not.")]
        public static partial void Finished(ILogger logger, Guid id, int @checked, int created, int merged, int dissolved, int matched, int notMatched);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Burst detection compared photo {Photo} with {Neighbour}: score {Score:F3}.")]
        public static partial void Compared(ILogger logger, Guid photo, Guid neighbour, double score);
    }
}
