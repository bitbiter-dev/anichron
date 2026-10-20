using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

public sealed record InteractionChange(bool? Starred, bool? Liked, bool? Hidden);

public sealed record InteractionState(bool Starred, bool Liked, bool Hidden);

/// <summary>
/// Applies a change to the caller's own interaction with an asset they can see; a <c>null</c> flag keeps its
/// stored value, and the row is created on the first change. Returns <c>null</c> when the asset is not visible
/// to the caller.
/// </summary>
public interface IAssetInteractionWriter
{
    Task<InteractionState?> ApplyAsync(Guid userId, Guid assetId, InteractionChange change, CancellationToken ct);
}

public sealed class EfAssetInteractionWriter(AnichronDbContext db) : IAssetInteractionWriter
{
    internal sealed record VisibleAsset(AssetInteraction? Interaction);

    public async Task<InteractionState?> ApplyAsync(Guid userId, Guid assetId, InteractionChange change, CancellationToken ct)
    {
        try
        {
            return await TryApplyAsync(userId, assetId, change, ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(InteractionIndexNames.OnePerUserAndAsset))
        {
            db.ChangeTracker.Clear();
            return await TryApplyAsync(userId, assetId, change, ct);
        }
    }

    private async Task<InteractionState?> TryApplyAsync(Guid userId, Guid assetId, InteractionChange change, CancellationToken ct)
    {
        var asset = await Query(db, userId, assetId).FirstOrDefaultAsync(ct);
        if (asset is null)
            return null;

        var interaction = asset.Interaction;
        if (interaction is null)
        {
            if (change is { Starred: null, Liked: null, Hidden: null })
                return new InteractionState(Starred: false, Liked: false, Hidden: false);

            interaction = new AssetInteraction { UserId = userId, AssetId = assetId };
            db.Interactions.Add(interaction);
        }

        interaction.IsStarred = change.Starred ?? interaction.IsStarred;
        interaction.IsLiked = change.Liked ?? interaction.IsLiked;
        interaction.IsHidden = change.Hidden ?? interaction.IsHidden;
        await db.SaveChangesAsync(ct);
        return new InteractionState(interaction.IsStarred, interaction.IsLiked, interaction.IsHidden);
    }

    internal static IQueryable<VisibleAsset> Query(AnichronDbContext db, Guid userId, Guid assetId)
        => db.MediaAssets.VisibleTo(userId)
            .Where(a => a.Id == assetId)
            .Select(a => new VisibleAsset(a.Interactions.FirstOrDefault(i => i.UserId == userId)));
}
