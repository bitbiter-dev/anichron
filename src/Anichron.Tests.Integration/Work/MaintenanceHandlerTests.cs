using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Core.Work;
using Anichron.Worker.Maintenance;
using Anichron.Worker.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Anichron.Tests.Integration.Work;

public sealed class MaintenanceHandlerTests(PostgresServer server)
{
    private static readonly Duration Lease = Duration.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TokenCleanup_SecondRun_ConvergesAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        await using (var db = work.CreateDbContext())
        {
            var user = new User { Id = Guid.NewGuid(), Username = "mira", Email = "mira@example.com", PasswordHash = "hash" };
            db.Users.Add(user);
            db.RefreshTokens.AddRange(
                new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "expired", ExpiresAt = WorkDatabase.Start - Duration.FromDays(1) },
                new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "valid", ExpiresAt = WorkDatabase.Start + Duration.FromDays(1) });
            await db.SaveChangesAsync(Ct);
        }

        var first = await RunTokenCleanupAsync(work);
        var second = await RunTokenCleanupAsync(work);

        (first, second).Should().Be((new TokenCleanupResult(1), new TokenCleanupResult(0)));
        await using var check = work.CreateDbContext();
        (await check.RefreshTokens.Select(t => t.TokenHash).ToListAsync(Ct)).Should().Equal("valid");
    }

    [Fact]
    public async Task WorkItemRetention_SecondRun_ConvergesAsync()
    {
        var work = await WorkDatabase.CreateAsync(server, Ct);
        var old = WorkDatabase.Start - Duration.FromDays(31);
        await work.EnqueueAsync(new UnkeyedPayload(1), Ct);
        var claimed = (await work.Store.ClaimAsync("worker", old, Lease, Ct))!;
        await work.Store.SucceedAsync(claimed.Attempt, WorkProgress.None, null, old, Ct);
        var pending = await work.EnqueueAsync(new UnkeyedPayload(2), Ct);

        var first = await RunRetentionAsync(work);
        var second = await RunRetentionAsync(work);

        var cutoff = WorkDatabase.Start - Duration.FromDays(30);
        (first, second).Should().Be((new WorkItemRetentionResult(1, cutoff), new WorkItemRetentionResult(0, cutoff)));
        (await work.ItemsAsync(Ct)).Select(w => w.Id).Should().Equal(pending);
    }

    private static async Task<object?> RunTokenCleanupAsync(WorkDatabase work)
    {
        await using var db = work.CreateDbContext();
        return await new TokenCleanupHandler(new EfRefreshTokenRepository(db), work.Clock, NullLogger<TokenCleanupHandler>.Instance)
            .RunAsync(new TokenCleanupPayload(), Substitute.For<IWorkProgress>(), Ct);
    }

    private static Task<object?> RunRetentionAsync(WorkDatabase work)
        => new WorkItemRetentionHandler(
                work.Store, work.Clock, Options.Create(new WorkerSettings()), NullLogger<WorkItemRetentionHandler>.Instance)
            .RunAsync(new WorkItemRetentionPayload(), Substitute.For<IWorkProgress>(), Ct);
}
