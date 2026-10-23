using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.IO.Abstractions;

namespace Anichron.Worker.Bursts;

public sealed record BurstDetectionResult(int Checked, int Created, int Merged, int Dissolved);

public sealed record BurstDetectionSkipped(string Reason);

internal sealed partial class BurstDetectionHandler(
    AnichronDbContext db,
    IOriginalPhotoReader reader,
    IFileSystem fileSystem,
    IClock clock,
    IGuidFactory guids,
    ILogger<BurstDetectionHandler> logger) : IWorkHandler<BurstDetectionPayload>
{
    internal const int BatchSize = 100;

    public async Task<object?> RunAsync(BurstDetectionPayload payload, IWorkProgress progress, CancellationToken ct)
    {
        var config = await db.StorageConfigs.FirstOrDefaultAsync(c => c.Id == payload.StorageConfigId, ct);
        if (config is not { IsActive: true })
        {
            Log.Skipped(logger, payload.StorageConfigId);
            return new BurstDetectionSkipped("The storage config is missing or inactive.");
        }

        if (!fileSystem.Directory.Exists(config.RootPath))
            throw new DirectoryNotFoundException($"The storage config's root {config.RootPath} is missing.");

        var tally = new Tally();
        await RepairAsync(config.Id, tally, ct);
        await CheckAsync(config, tally, progress, ct);

        var result = new BurstDetectionResult(tally.Checked, tally.Created, tally.Merged, tally.Dissolved);
        Log.Finished(logger, config.Id, result.Checked, result.Created, result.Merged, result.Dissolved);
        return result;
    }

    private async Task RepairAsync(Guid configId, Tally tally, CancellationToken ct)
    {
        var bursts = await db.Bursts.IgnoreQueryFilters()
            .Include(b => b.Assets)
            .Where(b => b.Assets.All(a => a.StorageConfigId == configId))
            .ToListAsync(ct);
        foreach (var burst in bursts)
        {
            if (burst.Assets.Count(a => !a.IsSoftDeleted) < 2)
            {
                db.Bursts.Remove(burst);
                tally.Dissolved++;
            }
            else
            {
                burst.PrimaryAssetId = Earliest(burst.Assets).Id;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task CheckAsync(UserStorageConfig config, Tally tally, IWorkProgress progress, CancellationToken ct)
    {
        var pending = db.MediaAssets.Where(a => a.StorageConfigId == config.Id
            && (a.MediaType == MediaType.Image || a.MediaType == MediaType.LivePhoto)
            && !db.BurstChecks.Any(c => c.AssetId == a.Id));
        var total = await pending.CountAsync(ct);

        while (await pending
            .Include(a => a.Metadata)
            .Include(a => a.ProxyFiles)
            .OrderBy(a => a.DateCaptured).ThenBy(a => a.Id)
            .Take(BatchSize)
            .ToListAsync(ct) is { Count: > 0 } batch)
        {
            foreach (var photo in batch)
            {
                await CheckAsync(config, photo, tally, ct);
                progress.Report(++tally.Checked, total, "Checking photos for bursts");
            }

            db.ChangeTracker.Clear();
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
        db.BurstChecks.Add(check);
        if (check is { IsCandidate: true, CameraBurstId: { } label })
            await JoinCameraBurstAsync(config.Id, photo, label, tally, ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task JoinCameraBurstAsync(Guid configId, MediaAsset photo, string label, Tally tally, CancellationToken ct)
    {
        var labelled = await db.BurstChecks
            .Where(c => c.IsCandidate && c.CameraBurstId == label && c.Asset.StorageConfigId == configId)
            .Select(c => c.Asset)
            .ToListAsync(ct);
        if (labelled.Count == 0)
            return;

        var burstIds = labelled.Select(a => a.BurstId).OfType<Guid>().ToList();
        var bursts = await db.Bursts.IgnoreQueryFilters()
            .Include(b => b.Assets)
            .Where(b => burstIds.Contains(b.Id))
            .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id)
            .ToListAsync(ct);
        List<MediaAsset> members = [.. bursts.SelectMany(b => b.Assets), .. labelled, photo];

        var target = bursts.FirstOrDefault();
        if (target is null)
        {
            target = new Burst { Id = guids.NewGuid(), CreatedAt = clock.GetCurrentInstant() };
            db.Bursts.Add(target);
            tally.Created++;
        }

        foreach (var merged in bursts.Skip(1))
        {
            db.Bursts.Remove(merged);
            tally.Merged++;
        }

        foreach (var member in members)
            member.BurstId = target.Id;
        target.PrimaryAssetId = Earliest(members).Id;
    }

    private static MediaAsset Earliest(IEnumerable<MediaAsset> members)
        => members.Where(a => !a.IsSoftDeleted).MinBy(a => (a.DateCaptured, a.Id))!;

    private sealed class Tally
    {
        public int Checked { get; set; }
        public int Created { get; set; }
        public int Merged { get; set; }
        public int Dissolved { get; set; }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Skipping burst detection for storage config {Id}: it is missing or inactive.")]
        public static partial void Skipped(ILogger logger, Guid id);

        [LoggerMessage(Level = LogLevel.Information, Message = "Burst detection for storage config {Id} checked {Checked} photo(s): {Created} burst(s) created, {Merged} merged, {Dissolved} dissolved.")]
        public static partial void Finished(ILogger logger, Guid id, int @checked, int created, int merged, int dissolved);
    }
}
