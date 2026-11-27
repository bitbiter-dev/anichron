using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data.Repository;

/// <summary>A storage config's failing files, written only by the Worker's crawl.</summary>
public interface IIngestionFailureRepository
{
    /// <summary>Every failing file of the config, tracked, so a change is written at the next save.</summary>
    /// <param name="storageConfigId">The config whose failures are loaded.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<IngestionFailure>> GetByConfigAsync(Guid storageConfigId, CancellationToken ct);

    void Add(IngestionFailure failure);
    void Remove(IngestionFailure failure);
}

public sealed class EfIngestionFailureRepository(AnichronDbContext db) : IIngestionFailureRepository
{
    public async Task<IReadOnlyList<IngestionFailure>> GetByConfigAsync(Guid storageConfigId, CancellationToken ct)
        => await db.IngestionFailures.Where(f => f.StorageConfigId == storageConfigId).ToListAsync(ct);

    public void Add(IngestionFailure failure) => db.IngestionFailures.Add(failure);

    public void Remove(IngestionFailure failure) => db.IngestionFailures.Remove(failure);
}
