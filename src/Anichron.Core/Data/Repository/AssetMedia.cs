using Anichron.Core.Domain;
using System.Linq.Expressions;

namespace Anichron.Core.Data.Repository;

public sealed record AssetMedia(
    Guid Id,
    string ContentHash,
    MediaType MediaType,
    int? Width,
    int? Height,
    int? Orientation,
    int? DurationInSeconds,
    string? Blurhash,
    bool HasThumbnail,
    bool HasPreview,
    bool HasCompactPreview,
    bool HasVideo,
    Guid? PairedMovId,
    string? PairedMovContentHash,
    bool PairedMovHasVideo)
{
    private static Expression<Func<MediaAsset, AssetMedia>> Of => a => new AssetMedia(
        a.Id,
        a.ContentHash,
        a.MediaType,
        a.Metadata!.Description.Width,
        a.Metadata.Description.Height,
        a.Metadata.Description.Orientation,
        a.Metadata.Description.DurationInSeconds,
        a.Metadata.Blurhash,
        a.ProxyFiles.Any(p => p.ProxyType == ProxyType.Thumbnail),
        a.ProxyFiles.Any(p => p.ProxyType == ProxyType.FullPreview),
        a.ProxyFiles.Any(p => p.ProxyType == ProxyType.CompactPreview),
        a.ProxyFiles.Any(p => p.ProxyType == ProxyType.WebVideo),
        a.PairedAsset!.Id,
        a.PairedAsset.ContentHash,
        a.PairedAsset.ProxyFiles.Any(p => p.ProxyType == ProxyType.WebVideo));

    /// <summary>A projection of a row into a view that carries the media of the row's asset.</summary>
    /// <typeparam name="TRow">The row the query reads.</typeparam>
    /// <typeparam name="TView">The view it projects into.</typeparam>
    /// <param name="asset">The row's asset.</param>
    /// <param name="view">The view, from the row and the asset's media.</param>
    internal static Expression<Func<TRow, TView>> Into<TRow, TView>(
        Expression<Func<TRow, MediaAsset>> asset, Expression<Func<TRow, AssetMedia, TView>> view)
    {
        var row = Expression.Parameter(typeof(TRow));
        var media = Expression.Invoke(Of, Expression.Invoke(asset, row));
        return Expression.Lambda<Func<TRow, TView>>(Expression.Invoke(view, row, media), row);
    }
}
