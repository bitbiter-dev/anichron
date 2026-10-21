using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Work;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Tests.Integration.Work;

public sealed record ScopedPayload(Guid ConfigId) : IWorkPayload
{
    public static WorkKind Kind => WorkKind.TokenCleanup;

    Guid? IWorkPayload.StorageConfigId => ConfigId;
}

public sealed class WorkScopeTests(PostgresServer server)
{
    private static readonly Duration Lease = Duration.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Instant At(int seconds) => WorkDatabase.Start + Duration.FromSeconds(seconds);

    private sealed record Library(WorkDatabase Work, Guid Mira, Guid MiraConfig, Guid Alice, Guid AliceConfig);

    private async Task<Library> LibraryAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var (mira, miraConfig, alice, aliceConfig) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = work.CreateDbContext();
        db.Users.AddRange(
            new User { Id = mira, Username = "mira", Email = "mira@example.com", PasswordHash = "hash" },
            new User { Id = alice, Username = "alice", Email = "alice@example.com", PasswordHash = "hash" });
        db.StorageConfigs.AddRange(
            new UserStorageConfig { Id = miraConfig, UserId = mira, RootPath = "/data/originals/mira", IsActive = true },
            new UserStorageConfig { Id = aliceConfig, UserId = alice, RootPath = "/data/originals/alice", IsActive = true });
        await db.SaveChangesAsync(Ct);
        return new Library(work, mira, miraConfig, alice, aliceConfig);
    }

    private static async Task<Guid> EnqueueAtAsync(WorkDatabase work, int seconds, IWorkPayload payload)
    {
        work.Clock.Now = At(seconds);
        return payload switch
        {
            ScopedPayload scoped => await work.EnqueueAsync(scoped, Ct),
            _ => await work.EnqueueAsync((UnkeyedPayload)payload, Ct),
        };
    }

    private static async Task<List<Guid>> ClaimAllAsync(WorkDatabase work, Guid? servedUserId)
    {
        var claimed = new List<Guid>();
        while (await work.Store.ClaimAsync("worker", servedUserId, At(100), Lease, Ct) is { } item)
            claimed.Add(item.Attempt.Id);
        return claimed;
    }

    [Fact]
    public async Task EnqueueAsync_StoresThePayloadsScopeAsync()
    {
        var library = await LibraryAsync();

        var id = await library.Work.EnqueueAsync(new ScopedPayload(library.MiraConfig), Ct);

        (await library.Work.ItemAsync(id, Ct)).StorageConfigId.Should().Be(library.MiraConfig);
    }

    [Fact]
    public async Task ClaimAsync_PerUserWorker_TakesItsOwnConfigsAndUnscopedItemsOnlyAsync()
    {
        var library = await LibraryAsync();
        var alices = await EnqueueAtAsync(library.Work, 0, new ScopedPayload(library.AliceConfig));
        var miras = await EnqueueAtAsync(library.Work, 1, new ScopedPayload(library.MiraConfig));
        var unscoped = await EnqueueAtAsync(library.Work, 2, new UnkeyedPayload(1));

        var claimed = await ClaimAllAsync(library.Work, library.Mira);

        claimed.Should().Equal(miras, unscoped);
        (await library.Work.ItemAsync(alices, Ct)).Status.Should().Be(WorkItemStatus.Queued);
    }

    [Fact]
    public async Task ClaimAsync_AllUserWorker_TakesEverythingAsync()
    {
        var library = await LibraryAsync();
        var alices = await EnqueueAtAsync(library.Work, 0, new ScopedPayload(library.AliceConfig));
        var miras = await EnqueueAtAsync(library.Work, 1, new ScopedPayload(library.MiraConfig));
        var unscoped = await EnqueueAtAsync(library.Work, 2, new UnkeyedPayload(1));

        var claimed = await ClaimAllAsync(library.Work, null);

        claimed.Should().Equal(alices, miras, unscoped);
    }

    [Fact]
    public async Task DeletingAConfig_DeletesItsItemsAndFencesTheRunningOneAsync()
    {
        var library = await LibraryAsync();
        await EnqueueAtAsync(library.Work, 0, new ScopedPayload(library.MiraConfig));
        var running = (await library.Work.Store.ClaimAsync("worker", library.Mira, At(1), Lease, Ct))!;
        await EnqueueAtAsync(library.Work, 2, new ScopedPayload(library.MiraConfig));
        var alices = await EnqueueAtAsync(library.Work, 3, new ScopedPayload(library.AliceConfig));

        await using (var db = library.Work.CreateDbContext())
            await db.StorageConfigs.Where(c => c.Id == library.MiraConfig).ExecuteDeleteAsync(Ct);

        (await library.Work.ItemsAsync(Ct)).Select(w => w.Id).Should().Equal(alices);
        (await library.Work.Store.HeartbeatAsync(running.Attempt, WorkProgress.None, At(5), Lease, Ct)).Should().Be(HeartbeatOutcome.Lost);
    }
}
