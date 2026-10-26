using Anichron.Core.Data;
using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Ingestion;

internal sealed class IngestionSaver(IUnitOfWork unitOfWork, IMediaAssetRepository repository)
{
    public async Task SaveAsync(IngestionContext context, Guid? pairedMovId, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            Release(context, pairedMovId);
            throw;
        }

        Release(context, pairedMovId);
    }

    public async Task<bool> TrySaveNewAsync(IngestionContext context, MediaAsset asset, Guid? pairedMovId, CancellationToken ct)
    {
        try
        {
            await SaveAsync(context, pairedMovId, ct);
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
    /// Soft-deletes <paramref name="retired"/> before saving the tracked changes, in one transaction, so a
    /// row can take the content the retired row held in the active content-hash index.
    /// </summary>
    /// <param name="retired">The row giving up its content.</param>
    /// <param name="ct">Cancels the save.</param>
    /// <returns>False when another consumer took the content first; nothing is saved then.</returns>
    public async Task<bool> TrySaveRetiringFirstAsync(MediaAsset retired, CancellationToken ct)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async () =>
                {
                    await repository.SoftDeleteAsync([retired.Id], ct);
                    await unitOfWork.SaveChangesAsync(ct);
                },
                ct);
            return true;
        }
        catch (DbUpdateException ex) when (LostIngestionRace.Matches(ex))
        {
            return false;
        }
    }

    private static void Release(IngestionContext context, Guid? pairedMovId)
    {
        if (pairedMovId.HasValue)
            context.ReleaseProxyFiles(pairedMovId.Value);
    }
}
