using Anichron.Core;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.ProxySweeps;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Work;

public sealed class ProxyOrphanStoreTests(PostgresServer server)
{
    private static readonly Duration Lease = Duration.FromSeconds(60);
    private static readonly Guid LiveConfigId = Guid.Parse("c1000000-0000-0000-0000-000000000000");
    private static readonly Guid GoneConfigId = Guid.Parse("c9000000-0000-0000-0000-000000000000");
    private static readonly Instant Written = Instant.FromUtc(2026, 10, 27, 9, 30, 15) + Duration.FromTicks(1_234_560);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(WorkDatabase Work, Guid SweepId)> SweepAsync(PostgresServer server)
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        return (work, await work.EnqueueAsync(new ProxySweepPayload(), Ct));
    }

    private static async Task RecordAsync(WorkDatabase work, Guid sweepId, params FoundOrphan[] orphans)
    {
        await using var db = work.CreateDbContext();
        await new EfProxyOrphanStore(db, new TimeOrderedGuidFactory()).RecordAsync(sweepId, orphans, Ct);
    }

    private static async Task<List<ProxyOrphan>> OrphansAsync(WorkDatabase work)
    {
        await using var db = work.CreateDbContext();
        return await db.ProxyOrphans.AsNoTracking().OrderBy(o => o.ProxyPath).ToListAsync(Ct);
    }

    [Fact]
    public async Task RecordAsync_StoresEachOrphanAsFoundAsync()
    {
        var (work, sweepId) = await SweepAsync(server);

        await RecordAsync(work, sweepId,
            new FoundOrphan("ab/x/thumbnail.jpg", LiveConfigId, 300, Written),
            new FoundOrphan("stray.txt", null, 4, Written));

        (await OrphansAsync(work)).Select(o => (o.SweepId, o.ProxyPath, o.StorageConfigId, o.SizeBytes, o.LastWriteTime, o.DeletionOutcome))
            .Should().Equal(
                (sweepId, "ab/x/thumbnail.jpg", LiveConfigId, 300L, Written, null),
                (sweepId, "stray.txt", null, 4L, Written, null));
    }

    [Fact]
    public async Task RecordAsync_RerunOfTheSweep_KeepsTheFirstRowsAndAddsOnlyNewPathsAsync()
    {
        var (work, sweepId) = await SweepAsync(server);
        await RecordAsync(work, sweepId, new FoundOrphan("ab/x/thumbnail.jpg", LiveConfigId, 300, Written));
        var first = (await OrphansAsync(work)).Single();

        await RecordAsync(work, sweepId,
            new FoundOrphan("ab/x/thumbnail.jpg", LiveConfigId, 999, Written),
            new FoundOrphan("ab/x/preview.jpg", LiveConfigId, 20, Written));

        var orphans = await OrphansAsync(work);
        orphans.Select(o => o.ProxyPath).Should().Equal("ab/x/preview.jpg", "ab/x/thumbnail.jpg");
        orphans[1].Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task RecordAsync_SamePathInAnotherSweep_IsItsOwnRowAsync()
    {
        var (work, sweepId) = await SweepAsync(server);
        var claimed = (await work.Store.ClaimAsync("worker", null, WorkDatabase.Start, Lease, Ct))!;
        await work.Store.SucceedAsync(claimed.Attempt, WorkProgress.None, null, WorkDatabase.Start, Ct);
        var secondSweep = await work.EnqueueAsync(new ProxySweepPayload(), Ct);

        await RecordAsync(work, sweepId, new FoundOrphan("stray.txt", null, 4, Written));
        await RecordAsync(work, secondSweep, new FoundOrphan("stray.txt", null, 4, Written));

        (await OrphansAsync(work)).Select(o => o.SweepId).Should().BeEquivalentTo([sweepId, secondSweep]);
    }

    [Fact]
    public async Task Retention_DeletingTheSweep_PurgesItsReportAsync()
    {
        var (work, sweepId) = await SweepAsync(server);
        var old = WorkDatabase.Start - Duration.FromDays(31);
        var claimed = (await work.Store.ClaimAsync("worker", null, old, Lease, Ct))!;
        await work.Store.SucceedAsync(claimed.Attempt, WorkProgress.None, null, old, Ct);
        await RecordAsync(work, sweepId, new FoundOrphan("stray.txt", null, 4, Written));

        await work.Store.DeleteFinishedBeforeAsync(WorkDatabase.Start - Duration.FromDays(30), Ct);

        (await OrphansAsync(work)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingAsync_Unscoped_ReadsOrphansOfNoExistingConfigAsync()
    {
        var (work, sweepId) = await SweepAsync(server);
        await using (var db = work.CreateDbContext())
        {
            var user = new User { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com", PasswordHash = "hash" };
            db.Users.Add(user);
            db.StorageConfigs.Add(new UserStorageConfig { Id = LiveConfigId, UserId = user.Id, RootPath = "/data/originals/mira", IsActive = true });
            await db.SaveChangesAsync(Ct);
        }

        await RecordAsync(work, sweepId,
            new FoundOrphan("ab/live/thumbnail.jpg", LiveConfigId, 1, Written),
            new FoundOrphan("ab/gone/thumbnail.jpg", GoneConfigId, 1, Written),
            new FoundOrphan("stray.txt", null, 1, Written));

        await using var read = work.CreateDbContext();
        var pending = await new EfProxyOrphanStore(read, new TimeOrderedGuidFactory())
            .GetPendingAsync(new ProxyDeletionPayload(sweepId, null, null), 10, Ct);
        pending.Select(o => o.ProxyPath).Should().BeEquivalentTo("ab/gone/thumbnail.jpg", "stray.txt");
    }

    [Fact]
    public async Task ReferencedUnderQuery_UsesThePathIndexAsync()
    {
        var connectionString = await server.CreateMigratedDatabaseAsync(Ct);
        await using var db = PostgresServer.Context(connectionString);
        var user = Guid.NewGuid();
        db.Users.Add(new User { Id = user, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" });
        db.StorageConfigs.Add(new UserStorageConfig { Id = LiveConfigId, UserId = user, RootPath = "/data/originals/mira", IsActive = true });
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH assets AS (
                INSERT INTO "MediaAssets" ("Id", "StorageConfigId", "FilePath", "FileName", "ContentHash", "DateCaptured",
                    "Month", "Day", "Year", "MediaType", "IsSoftDeleted", "LastSeenOnNas")
                SELECT gen_random_uuid(), {LiveConfigId}, n || '.jpg', n || '.jpg', 'hash' || n, '2024-01-01',
                    1, 1, 2024, 'Image', false, now()
                FROM generate_series(1, 5000) AS n
                RETURNING "Id", "ContentHash")
            INSERT INTO "ProxyFiles" ("Id", "AssetId", "ProxyPath", "ProxyType", "SizeBytes", "CreatedAt")
            SELECT gen_random_uuid(), "Id", lpad(to_hex(abs(hashtext("ContentHash")) % 256), 2, '0') || '/' || "ContentHash" || '/thumbnail.jpg',
                'Thumbnail', 1, now()
            FROM assets
            """, Ct);
        await db.Database.ExecuteSqlRawAsync("""ANALYZE "ProxyFiles" """, Ct);
        await using var command = EfProxyOrphanStore.ReferencedUnderQuery(db, "ab").CreateDbCommand();
        await db.Database.OpenConnectionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET enable_seqscan = off", Ct);
        command.CommandText = "EXPLAIN " + command.CommandText;

        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
                plan.Add(reader.GetString(0));
        }

        plan.Should().NotContain(line => line.Contains("Seq Scan"));
        string.Join('\n', plan).Should().Contain("ix_proxyfiles_proxypath");
    }
}
