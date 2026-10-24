using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Work;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Bursts;

public sealed record BurstDetectionResult
{
    public int Checked { get; set; }
    public int Created { get; set; }
    public int Merged { get; set; }
    public int Dissolved { get; set; }
}

internal sealed partial class BurstDetectionHandler(
    ActiveStorageConfigs activeConfigs,
    IBurstRepository bursts,
    IUnitOfWork unitOfWork,
    IOriginalPhotoReader reader,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guids,
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

        Log.Finished(logger, config.Id, result.Checked, result.Created, result.Merged, result.Dissolved);
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
        if (check is { IsCandidate: true, CameraBurstId: { } label })
            await JoinCameraBurstAsync(config.Id, photo, label, result, ct);

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task JoinCameraBurstAsync(
        Guid configId, MediaAsset photo, string label, BurstDetectionResult result, CancellationToken ct)
    {
        var labelled = await bursts.GetCandidatesLabelledAsync(configId, label, ct);
        if (labelled.Count == 0)
            return;

        var joined = await bursts.GetWithMembersAsync([.. labelled.Select(a => a.BurstId).OfType<Guid>()], ct);
        List<MediaAsset> members = [.. joined.SelectMany(b => b.Assets), .. labelled, photo];

        if (joined is not [var target, ..])
        {
            target = new Burst { Id = guids.NewGuid(), CreatedAt = clock.GetCurrentInstant() };
            bursts.Add(target);
            result.Created++;
        }

        foreach (var merged in joined.Skip(1))
        {
            bursts.Remove(merged);
            result.Merged++;
        }

        foreach (var member in members)
            member.BurstId = target.Id;
        target.PrimaryAssetId = Earliest(members).Id;
    }

    private static MediaAsset Earliest(IEnumerable<MediaAsset> members)
        => members.Where(a => !a.IsSoftDeleted).MinBy(a => (a.DateCaptured, a.Id))!;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Burst detection for storage config {Id} checked {Checked} photo(s): {Created} burst(s) created, {Merged} merged, {Dissolved} dissolved.")]
        public static partial void Finished(ILogger logger, Guid id, int @checked, int created, int merged, int dissolved);
    }
}
