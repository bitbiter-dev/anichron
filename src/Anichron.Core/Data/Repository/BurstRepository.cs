using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

/// <summary>Bursts and the burst-checked mark, written only by the Worker's burst detection.</summary>
public interface IBurstRepository
{
    /// <summary>The config's bursts, each with every member, soft-deleted ones included.</summary>
    /// <param name="storageConfigId">The config whose bursts are loaded.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<Burst>> GetInConfigWithMembersAsync(Guid storageConfigId, CancellationToken ct);

    /// <summary>The bursts with every member.</summary>
    /// <param name="burstIds">The bursts to load.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<Burst>> GetWithMembersAsync(IReadOnlyCollection<Guid> burstIds, CancellationToken ct);

    /// <summary>
    /// Deletes every burst without a member row, in any config (left behind when a storage config
    /// was deleted), in one statement, so two Workers repairing at once cannot conflict.
    /// </summary>
    /// <param name="ct">Cancels the statement.</param>
    /// <returns>How many bursts were deleted.</returns>
    Task<int> DeleteMemberlessAsync(CancellationToken ct);

    Task<int> CountUncheckedPhotosAsync(Guid storageConfigId, CancellationToken ct);

    /// <summary>
    /// The config's oldest active photos without a burst-checked mark, with their metadata and
    /// proxy rows, by capture time and then id.
    /// </summary>
    /// <param name="storageConfigId">The config whose photos are loaded.</param>
    /// <param name="after">Only photos that come after this one in that order; null starts at the oldest.</param>
    /// <param name="count">At most this many.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> GetUncheckedPhotosAsync(
        Guid storageConfigId, MediaAsset? after, int count, CancellationToken ct);

    /// <summary>The config's checked candidates the camera labelled with this burst id.</summary>
    /// <param name="storageConfigId">The config searched.</param>
    /// <param name="cameraBurstId">Apple's BurstUUID.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> GetCandidatesLabelledAsync(Guid storageConfigId, string cameraBurstId, CancellationToken ct);

    /// <summary>
    /// The config's checked candidates from this camera captured within the window that still have
    /// a thumbnail, each with its proxy rows, oldest first (then by id).
    /// </summary>
    /// <param name="storageConfigId">The config searched.</param>
    /// <param name="cameraMake">The camera make the candidates share.</param>
    /// <param name="cameraModel">The camera model the candidates share.</param>
    /// <param name="from">The earliest capture time, inclusive.</param>
    /// <param name="to">The latest capture time, inclusive.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> GetCandidatesNearAsync(
        Guid storageConfigId, string cameraMake, string cameraModel, LocalDateTime from, LocalDateTime to, CancellationToken ct);

    void Add(Burst burst);
    void Remove(Burst burst);
    void AddCheck(BurstCheck check);

    /// <summary>Clears the asset's burst-checked mark, if it has one, at the next save.</summary>
    /// <param name="assetId">The asset whose mark is cleared.</param>
    /// <param name="ct">Cancels the lookup.</param>
    Task RemoveCheckAsync(Guid assetId, CancellationToken ct);

    /// <summary>Stops tracking everything loaded so far, so a long run does not hold it all.</summary>
    void ForgetLoaded();
}

public sealed class EfBurstRepository(AnichronDbContext db) : IBurstRepository
{
    public async Task<IReadOnlyList<Burst>> GetInConfigWithMembersAsync(Guid storageConfigId, CancellationToken ct)
        => await WithMembers()
            .Where(b => b.Assets.Any(a => a.StorageConfigId == storageConfigId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Burst>> GetWithMembersAsync(IReadOnlyCollection<Guid> burstIds, CancellationToken ct)
        => await WithMembers()
            .Where(b => burstIds.Contains(b.Id))
            .ToListAsync(ct);

    // Stryker disable all
    public Task<int> DeleteMemberlessAsync(CancellationToken ct)
        => db.Bursts.IgnoreQueryFilters()
            .Where(b => !db.MediaAssets.IgnoreQueryFilters().Any(a => a.BurstId == b.Id))
            .ExecuteDeleteAsync(ct);

    public Task<int> CountUncheckedPhotosAsync(Guid storageConfigId, CancellationToken ct)
        => UncheckedPhotos(storageConfigId).CountAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> GetUncheckedPhotosAsync(
        Guid storageConfigId, MediaAsset? after, int count, CancellationToken ct)
        => await UncheckedPhotosAfter(storageConfigId, after)
            .Include(a => a.Metadata)
            .Include(a => a.ProxyFiles)
            .OrderBy(a => a.DateCaptured).ThenBy(a => a.Id)
            .Take(count)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> GetCandidatesLabelledAsync(
        Guid storageConfigId, string cameraBurstId, CancellationToken ct)
        => await CandidatesLabelled(storageConfigId, cameraBurstId).ToListAsync(ct);

    internal IQueryable<MediaAsset> CandidatesLabelled(Guid storageConfigId, string cameraBurstId)
        => db.BurstChecks
            .IgnoreQueryFilters()
            .Where(c => c.IsCandidate && c.CameraBurstId == cameraBurstId
                && c.Asset.StorageConfigId == storageConfigId && !c.Asset.IsSoftDeleted)
            .Select(c => c.Asset);

    public async Task<IReadOnlyList<MediaAsset>> GetCandidatesNearAsync(
        Guid storageConfigId, string cameraMake, string cameraModel, LocalDateTime from, LocalDateTime to, CancellationToken ct)
        => await CandidatesNear(storageConfigId, cameraMake, cameraModel, from, to).ToListAsync(ct);

    internal IQueryable<MediaAsset> CandidatesNear(
        Guid storageConfigId, string cameraMake, string cameraModel, LocalDateTime from, LocalDateTime to)
        => db.MediaAssets
            .IgnoreQueryFilters()
            .Include(a => a.ProxyFiles)
            .Where(a => a.StorageConfigId == storageConfigId && !a.IsSoftDeleted
                && a.DateCaptured >= from && a.DateCaptured <= to
                && a.Metadata!.CameraMake == cameraMake && a.Metadata.CameraModel == cameraModel
                && a.ProxyFiles.Any(p => p.ProxyType == ProxyType.Thumbnail)
                && db.BurstChecks.Any(c => c.AssetId == a.Id && c.IsCandidate))
            .OrderBy(a => a.DateCaptured).ThenBy(a => a.Id);

    public void Add(Burst burst)
        => db.Bursts.Add(burst);

    public void Remove(Burst burst)
        => db.Bursts.Remove(burst);

    public void AddCheck(BurstCheck check)
        => db.BurstChecks.Add(check);

    public async Task RemoveCheckAsync(Guid assetId, CancellationToken ct)
    {
        if (await db.BurstChecks.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.AssetId == assetId, ct) is { } check)
            db.BurstChecks.Remove(check);
    }

    public void ForgetLoaded()
        => db.ChangeTracker.Clear();

    private IQueryable<Burst> WithMembers()
        => db.Bursts.IgnoreQueryFilters().Include(b => b.Assets);

    internal IQueryable<MediaAsset> UncheckedPhotosAfter(Guid storageConfigId, MediaAsset? after)
    {
        if (after is null)
            return UncheckedPhotos(storageConfigId);

        var (capturedAt, id) = (after.DateCaptured, after.Id);
        return UncheckedPhotos(storageConfigId)
            .Where(a => a.DateCaptured > capturedAt || (a.DateCaptured == capturedAt && a.Id.CompareTo(id) > 0));
    }

    internal IQueryable<MediaAsset> UncheckedPhotos(Guid storageConfigId)
        => db.MediaAssets.IgnoreQueryFilters().Where(a => a.StorageConfigId == storageConfigId && !a.IsSoftDeleted
            && (a.MediaType == MediaType.Image || a.MediaType == MediaType.LivePhoto)
            && !db.BurstChecks.Any(c => c.AssetId == a.Id));
}
