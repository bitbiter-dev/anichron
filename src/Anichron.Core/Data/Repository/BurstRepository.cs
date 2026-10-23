using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

/// <summary>Bursts and the burst-checked mark, written only by the Worker's burst detection.</summary>
public interface IBurstRepository
{
    /// <summary>
    /// The config's bursts, and bursts with no members left, each with every member, soft-deleted
    /// ones included.
    /// </summary>
    /// <param name="storageConfigId">The config whose bursts are loaded.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<Burst>> GetInConfigWithMembersAsync(Guid storageConfigId, CancellationToken ct);

    /// <summary>The bursts with every member, oldest first (then by id).</summary>
    /// <param name="burstIds">The bursts to load.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<Burst>> GetWithMembersAsync(IReadOnlyCollection<Guid> burstIds, CancellationToken ct);

    Task<int> CountUncheckedPhotosAsync(Guid storageConfigId, CancellationToken ct);

    /// <summary>
    /// The config's oldest active photos without a burst-checked mark, with their metadata and
    /// proxy rows.
    /// </summary>
    /// <param name="storageConfigId">The config whose photos are loaded.</param>
    /// <param name="count">At most this many.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> GetUncheckedPhotosAsync(Guid storageConfigId, int count, CancellationToken ct);

    /// <summary>The config's checked candidates the camera labelled with this burst id.</summary>
    /// <param name="storageConfigId">The config searched.</param>
    /// <param name="cameraBurstId">Apple's BurstUUID.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<MediaAsset>> GetCandidatesLabelledAsync(Guid storageConfigId, string cameraBurstId, CancellationToken ct);

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
            .Where(b => b.Assets.All(a => a.StorageConfigId == storageConfigId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Burst>> GetWithMembersAsync(IReadOnlyCollection<Guid> burstIds, CancellationToken ct)
        => await WithMembers()
            .Where(b => burstIds.Contains(b.Id))
            .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id)
            .ToListAsync(ct);

    public Task<int> CountUncheckedPhotosAsync(Guid storageConfigId, CancellationToken ct)
        => UncheckedPhotos(storageConfigId).CountAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> GetUncheckedPhotosAsync(Guid storageConfigId, int count, CancellationToken ct)
        => await UncheckedPhotos(storageConfigId)
            .Include(a => a.Metadata)
            .Include(a => a.ProxyFiles)
            .OrderBy(a => a.DateCaptured).ThenBy(a => a.Id)
            .Take(count)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MediaAsset>> GetCandidatesLabelledAsync(
        Guid storageConfigId, string cameraBurstId, CancellationToken ct)
        => await db.BurstChecks
            .Where(c => c.IsCandidate && c.CameraBurstId == cameraBurstId && c.Asset.StorageConfigId == storageConfigId)
            .Select(c => c.Asset)
            .ToListAsync(ct);

    public void Add(Burst burst)
        => db.Bursts.Add(burst);

    public void Remove(Burst burst)
        => db.Bursts.Remove(burst);

    public void AddCheck(BurstCheck check)
        => db.BurstChecks.Add(check);

    public async Task RemoveCheckAsync(Guid assetId, CancellationToken ct)
    {
        if (await db.BurstChecks.FindAsync([assetId], ct) is { } check)
            db.BurstChecks.Remove(check);
    }

    public void ForgetLoaded()
        => db.ChangeTracker.Clear();

    private IQueryable<Burst> WithMembers()
        => db.Bursts.IgnoreQueryFilters().Include(b => b.Assets);

    private IQueryable<MediaAsset> UncheckedPhotos(Guid storageConfigId)
        => db.MediaAssets.Where(a => a.StorageConfigId == storageConfigId
            && (a.MediaType == MediaType.Image || a.MediaType == MediaType.LivePhoto)
            && !db.BurstChecks.Any(c => c.AssetId == a.Id));
}
