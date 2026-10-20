using Anichron.Core.Data;
using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Anichron.Core.Work;

public sealed record WorkItemView(
    Guid Id,
    WorkKind Kind,
    Guid? StorageConfigId,
    Guid? RequestedBy,
    Instant RequestedAt,
    WorkItemStatus Status,
    int Attempt,
    int Stalls,
    string? ClaimedBy,
    Instant? StartedAt,
    Instant? FinishedAt,
    Instant UpdatedAt,
    long ProgressDone,
    long? ProgressTotal,
    string? ProgressNote,
    string? Result,
    string? Error);

public sealed record WorkItemQuery(WorkItemStatus? Status, WorkKind? Kind, int Skip, int Take);

public sealed record WorkItemPage(IReadOnlyList<WorkItemView> Items, int Total);

public interface IWorkItemReader
{
    /// <summary>A page of items, newest request first.</summary>
    /// <param name="query">The optional status and kind filters, and the page.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The page, and the number of items matching the filters.</returns>
    Task<WorkItemPage> ListAsync(WorkItemQuery query, CancellationToken ct);

    Task<WorkItemView?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>Every item that changed after a point in time, oldest change first.</summary>
    /// <param name="since">Only items whose <see cref="WorkItem.UpdatedAt"/> is later are returned.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The changed items, ordered by <see cref="WorkItem.UpdatedAt"/> and then id.</returns>
    Task<IReadOnlyList<WorkItemView>> UpdatedSinceAsync(Instant since, CancellationToken ct);

    /// <summary>The newest <see cref="WorkItem.UpdatedAt"/> of any item.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The newest change time, or null when there are no items.</returns>
    Task<Instant?> LatestUpdateAsync(CancellationToken ct);
}

public sealed class EfWorkItemReader(AnichronDbContext db) : IWorkItemReader
{
    public async Task<WorkItemPage> ListAsync(WorkItemQuery query, CancellationToken ct)
    {
        var matching = db.WorkItems.AsQueryable();
        if (query.Status is { } status)
            matching = matching.Where(w => w.Status == status);
        if (query.Kind is { } kind)
            matching = matching.Where(w => w.Kind == kind);

        var items = await matching
            .OrderByDescending(w => w.RequestedAt)
            .ThenByDescending(w => w.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(ToView)
            .ToListAsync(ct);
        return new WorkItemPage(items, await matching.CountAsync(ct));
    }

    public Task<WorkItemView?> FindAsync(Guid id, CancellationToken ct)
        => db.WorkItems.Where(w => w.Id == id).Select(ToView).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<WorkItemView>> UpdatedSinceAsync(Instant since, CancellationToken ct)
        => await UpdatedSinceQuery(db, since).ToListAsync(ct);

    public Task<Instant?> LatestUpdateAsync(CancellationToken ct)
        => db.WorkItems.MaxAsync(w => (Instant?)w.UpdatedAt, ct);

    internal static IQueryable<WorkItemView> UpdatedSinceQuery(AnichronDbContext db, Instant since)
        => db.WorkItems
            .Where(w => w.UpdatedAt > since)
            .OrderBy(w => w.UpdatedAt)
            .ThenBy(w => w.Id)
            .Select(ToView);

    private static Expression<Func<WorkItem, WorkItemView>> ToView
        => w => new WorkItemView(
            w.Id, w.Kind, w.StorageConfigId, w.RequestedBy, w.RequestedAt, w.Status, w.Attempt, w.Stalls,
            w.ClaimedBy, w.StartedAt, w.FinishedAt, w.UpdatedAt, w.ProgressDone, w.ProgressTotal,
            w.ProgressNote, w.Result, w.Error);
}
