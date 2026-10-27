using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Classification;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Ingestion;

internal sealed class IngestionSaver(IUnitOfWork unitOfWork, IMediaAssetRepository repository)
{
    public Task SaveAsync(IngestionContext context, Guid? pairedMovId, CancellationToken ct)
        => SaveAsync(context, [], pairedMovId, ct);

    /// <summary>
    /// Saves a row with newly processed content, after the classification's <see cref="IngestionClassification.RetiredFirst"/> rows.
    /// </summary>
    /// <param name="context">The classified item whose staged counts the save commits.</param>
    /// <param name="asset">The row with the processed content.</param>
    /// <param name="pairedMovId">The paired video, whose proxies the attempt releases.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>False when another consumer saved the same content first; nothing is saved then.</returns>
    public async Task<bool> TrySaveNewAsync(IngestionContext context, MediaAsset asset, Guid? pairedMovId, CancellationToken ct)
    {
        try
        {
            await SaveAsync(context, context.Classification!.RetiredFirst, pairedMovId, ct);
            return true;
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            if (await repository.FindByHashAsync(asset.ContentHash, asset.StorageConfigId, ct) is null)
                throw;
            return false;
        }
    }

    /// <summary>
    /// Soft-deletes the classification's <see cref="IngestionClassification.RetiredFirst"/> rows before saving the
    /// tracked changes, in one transaction, so rows can take the content the retired rows held in the active
    /// content-hash index.
    /// </summary>
    /// <param name="context">The item whose staged counts the save commits.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>False when another consumer took the content first; nothing is saved then.</returns>
    public async Task<bool> TrySaveRetiringFirstAsync(IngestionContext context, CancellationToken ct)
    {
        try
        {
            await SaveAsync(context, context.Classification!.RetiredFirst, null, ct);
            return true;
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            return false;
        }
    }

    private async Task SaveAsync(
        IngestionContext context, IReadOnlyList<MediaAsset> retiredFirst, Guid? pairedMovId, CancellationToken ct)
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
            Release(context, pairedMovId);
            throw;
        }

        context.CommitCounts();
        Release(context, pairedMovId);
    }

    private Task SaveRetiringFirstAsync(IReadOnlyList<MediaAsset> retired, CancellationToken ct)
        => unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await repository.SoftDeleteAsync([.. retired.Select(row => row.Id)], ct);
                await unitOfWork.SaveChangesAsync(ct);
            },
            ct);

    private static void Release(IngestionContext context, Guid? pairedMovId)
    {
        if (pairedMovId.HasValue)
            context.ReleaseProxyFiles(pairedMovId.Value);
    }
}
