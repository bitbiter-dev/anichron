using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using NodaTime;

namespace Anichron.Worker.Ingestion.Classification;

/// <summary>The content a row takes, from processing a file or from another row.</summary>
/// <param name="ContentHash">The content's hash.</param>
/// <param name="DateCaptured">The content's capture date.</param>
/// <param name="MediaType">The content's media type.</param>
/// <param name="Description">The metadata values; a stored row copies them, a new row takes this row as its own.</param>
/// <param name="Proxies">The content's proxies, owned by the row that takes it.</param>
internal sealed record AssetContent(
    string ContentHash, LocalDateTime DateCaptured, MediaType MediaType, Metadata Description, IReadOnlyList<ProxyFile> Proxies)
{
    public static AssetContent Of(MediaAsset source, IReadOnlyList<ProxyFile> proxies)
        => new(source.ContentHash, source.DateCaptured, source.MediaType, source.Metadata ?? new Metadata(), proxies);

    public void ApplyTo(MediaAsset asset)
    {
        asset.ContentHash = ContentHash;
        asset.DateCaptured = DateCaptured;
        asset.MediaType = MediaType;
    }
}

internal sealed class StoredContent(IMediaAssetRepository repository, IBurstRepository bursts, IClock clock)
{
    /// <summary>
    /// Makes <paramref name="asset"/> hold <paramref name="content"/>: its content fields, metadata and proxy rows,
    /// out of its burst, and active.
    /// </summary>
    /// <param name="context">The item that stages the auto-restore count.</param>
    /// <param name="asset">A tracked row.</param>
    /// <param name="content">The content it takes.</param>
    /// <param name="ct">Cancels the lookups.</param>
    /// <returns>Whether the row was soft-deleted.</returns>
    public async Task<bool> TakeAsync(IngestionContext context, MediaAsset asset, AssetContent content, CancellationToken ct)
    {
        await repository.LoadMetadataAsync(asset, ct);
        content.ApplyTo(asset);
        MetadataValues.Copy(content.Description, asset.Metadata ??= new Metadata { AssetId = asset.Id });
        ProxyFileRows.Replace(repository, asset, content.Proxies);
        asset.BurstId = null;
        return await ReactivateAsync(context, asset, ct);
    }

    /// <summary>Makes the row active and seen now, and clears its burst-checked mark.</summary>
    /// <param name="context">The item that stages the auto-restore count.</param>
    /// <param name="asset">A tracked row.</param>
    /// <param name="ct">Cancels the lookup.</param>
    /// <returns>Whether the row was soft-deleted.</returns>
    public async Task<bool> ReactivateAsync(IngestionContext context, MediaAsset asset, CancellationToken ct)
    {
        var wasSoftDeleted = asset.IsSoftDeleted;
        if (wasSoftDeleted)
            context.StageAutoRestored();
        asset.IsSoftDeleted = false;
        asset.LastSeenOnNas = clock.GetCurrentInstant();
        await bursts.RemoveCheckAsync(asset.Id, ct);
        return wasSoftDeleted;
    }
}
