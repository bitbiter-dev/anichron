using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record AssetView(
    Guid Id,
    MediaType MediaType,
    int Year,
    int Month,
    int Day,
    LocalDateTime DateCaptured,
    int? Width,
    int? Height,
    int? Orientation,
    int? DurationInSeconds,
    string? Blurhash,
    bool HasThumbnail,
    bool HasPreview,
    bool HasVideo,
    Guid? PairedMovId,
    bool PairedMovHasVideo,
    bool IsStarred,
    bool IsLiked,
    Guid? BurstId);

/// <summary>Narrows the assets an <see cref="IAssetViewReader"/> returns, filtering on entity columns only.</summary>
public interface IAssetSelection
{
    IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets);
}

public sealed record OnThisDay(LocalDate Date) : IAssetSelection
{
    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets)
    {
        var onTheDay = assets.Where(a => a.Month == Date.Month && a.Day == Date.Day && a.Year < Date.Year);
        return onTheDay.Union(
            from d in onTheDay
            where d.BurstId != null
            from a in assets
            where a.BurstId == d.BurstId
            select a);
    }
}

public sealed record BurstMembers(Guid BurstId) : IAssetSelection
{
    public IQueryable<MediaAsset> Apply(IQueryable<MediaAsset> assets)
        => assets.Where(a => a.BurstId == BurstId);
}

/// <summary>
/// Reads the caller's visible, not-hidden assets matching a selection, with the caller's own interaction
/// state, in one query.
/// </summary>
public interface IAssetViewReader
{
    Task<IReadOnlyList<AssetView>> ReadAsync(Guid userId, IAssetSelection selection, CancellationToken ct);
}

public sealed class EfAssetViewReader(AnichronDbContext db) : IAssetViewReader
{
    public async Task<IReadOnlyList<AssetView>> ReadAsync(Guid userId, IAssetSelection selection, CancellationToken ct)
        => await Query(db, userId, selection).ToListAsync(ct);

    internal static IQueryable<AssetView> Query(AnichronDbContext db, Guid userId, IAssetSelection selection)
        => from a in selection.Apply(db.MediaAssets.VisibleTo(userId).NotHiddenFor(userId))
           join i in db.Interactions.Where(i => i.UserId == userId) on a.Id equals i.AssetId into interactions
           from i in interactions.DefaultIfEmpty()
           select new AssetView(
               a.Id,
               a.MediaType,
               a.Year,
               a.Month,
               a.Day,
               a.DateCaptured,
               a.Metadata!.Width,
               a.Metadata.Height,
               a.Metadata.Orientation,
               a.Metadata.DurationInSeconds,
               a.Metadata.Blurhash,
               a.ProxyFiles.Any(p => p.ProxyType == ProxyType.Thumbnail),
               a.ProxyFiles.Any(p => p.ProxyType == ProxyType.FullPreview),
               a.ProxyFiles.Any(p => p.ProxyType == ProxyType.WebVideo),
               a.PairedAsset!.Id,
               a.PairedAsset.ProxyFiles.Any(p => p.ProxyType == ProxyType.WebVideo),
               i != null && i.IsStarred,
               i != null && i.IsLiked,
               a.BurstId);
}
