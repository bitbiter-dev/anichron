using Anichron.Core;
using Anichron.Core.Data;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Work;

public sealed record UnkeyedPayload(int Number) : IWorkPayload
{
    public static WorkKind Kind => WorkKind.TokenCleanup;
}

public sealed record KeyedPayload(string Key) : IWorkPayload
{
    public static WorkKind Kind => WorkKind.TokenCleanup;

    string IWorkPayload.DedupKey => Key;
}

internal sealed class StubClock(Instant now) : IClock
{
    public Instant Now { get; set; } = now;

    public Instant GetCurrentInstant() => Now;
}

internal sealed class WorkDatabase(string connectionString) : IDbContextFactory<AnichronDbContext>
{
    public static readonly Instant Start = Instant.FromUtc(2026, 10, 18, 9, 0);

    public StubClock Clock { get; } = new(Start);

    public EfWorkItemStore Store => new(this);

    public static async Task<WorkDatabase> CreateAsync(PostgresServer server, CancellationToken ct)
        => new(await server.CreateMigratedDatabaseAsync(ct));

    public AnichronDbContext CreateDbContext() => PostgresServer.Context(connectionString);

    public async Task<Guid> EnqueueAsync<TPayload>(TPayload payload, CancellationToken ct)
        where TPayload : IWorkPayload
    {
        await using var db = CreateDbContext();
        return await new EfWorkQueue(db, Clock, new TimeOrderedGuidFactory()).EnqueueAsync(payload, null, ct);
    }

    public async Task<WorkItem> ItemAsync(Guid id, CancellationToken ct)
    {
        await using var db = CreateDbContext();
        return await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == id, ct);
    }

    public async Task<List<WorkItem>> ItemsAsync(CancellationToken ct)
    {
        await using var db = CreateDbContext();
        return await db.WorkItems.AsNoTracking().OrderBy(w => w.RequestedAt).ToListAsync(ct);
    }
}
