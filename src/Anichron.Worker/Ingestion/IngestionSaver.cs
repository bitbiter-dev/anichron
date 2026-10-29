using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Ingestion;

internal sealed class IngestionSaver(IUnitOfWork unitOfWork, IMediaAssetRepository repository)
{
    /// <summary>
    /// Saves everything the item staged, in its one save: when the classification has
    /// <see cref="IngestionClassification.RetiredFirst"/> rows, they are soft-deleted first, in one transaction, so rows
    /// can take the content they held in the active content-hash index. After a commit, and on a lost race, the proxies of
    /// every staged video are released.
    /// </summary>
    /// <param name="context">The classified item whose staged changes and counts the save commits.</param>
    /// <param name="processed">The row with newly processed content; null when the item only reconciled stored rows.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>
    /// False when another consumer took the content first, for a processed row whose content another row committed or
    /// for an item that retires rows first; nothing is saved then.
    /// </returns>
    public async Task<bool> TrySaveItemAsync(IngestionContext context, MediaAsset? processed, CancellationToken ct)
    {
        var retiredFirst = context.Classification!.RetiredFirst;
        try
        {
            await SaveAsync(context, retiredFirst, ct);
            return true;
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex) && (processed is not null || retiredFirst.Count > 0))
        {
            if (processed is not null && await repository.FindByHashAsync(processed.ContentHash, processed.StorageConfigId, ct) is null)
                throw;
            return false;
        }
    }

    private async Task SaveAsync(IngestionContext context, IReadOnlyList<MediaAsset> retiredFirst, CancellationToken ct)
    {
        try
        {
            if (retiredFirst.Count == 0)
                await unitOfWork.SaveChangesAsync(ct);
            else
                await SaveRetiringFirstAsync(retiredFirst, ct);
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            context.ReleaseStagedMovProxies();
            throw;
        }

        context.CommitCounts();
        context.ReleaseStagedMovProxies();
    }

    private Task SaveRetiringFirstAsync(IReadOnlyList<MediaAsset> retired, CancellationToken ct)
        => unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await repository.SoftDeleteAsync([.. retired.Select(row => row.Id)], ct);
                await unitOfWork.SaveChangesAsync(ct);
            },
            ct);
}
