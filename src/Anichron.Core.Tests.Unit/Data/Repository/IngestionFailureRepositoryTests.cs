using Anichron.Core.Data.Repository;
using Anichron.Core.Domain;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Anichron.Core.Tests.Unit.Data.Repository;

public sealed class IngestionFailureRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IngestionFailure Failure(Guid config, string path) => new()
    {
        StorageConfigId = config,
        FilePath = path,
        Fingerprint = "1:2",
        Failures = 1,
        Reason = "IOException: boom",
        FirstFailedAt = Instant.FromUtc(2026, 11, 1, 0, 0),
        LastFailedAt = Instant.FromUtc(2026, 11, 1, 0, 0),
        RetryAfter = Instant.FromUtc(2026, 11, 1, 2, 0),
    };

    [Fact]
    public async Task GetByConfigAsync_ReturnsOnlyThatConfigsRowsTrackedAsync()
    {
        var name = Guid.NewGuid().ToString();
        var (config, other) = (Guid.NewGuid(), Guid.NewGuid());
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.IngestionFailures.AddRange(Failure(config, "a.jpg"), Failure(config, "b.jpg"), Failure(other, "a.jpg"));
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = AnichronDbContexts.InMemory(name);
        var rows = await new EfIngestionFailureRepository(db).GetByConfigAsync(config, Ct);

        rows.Select(row => (row.StorageConfigId, row.FilePath)).Should().BeEquivalentTo([(config, "a.jpg"), (config, "b.jpg")]);
        rows.Should().OnlyContain(row => db.Entry(row).State == EntityState.Unchanged);
    }

    [Fact]
    public async Task AddAndRemove_AreWrittenAtTheNextSaveAsync()
    {
        var name = Guid.NewGuid().ToString();
        var config = Guid.NewGuid();
        await using (var seed = AnichronDbContexts.InMemory(name))
        {
            seed.IngestionFailures.Add(Failure(config, "old.jpg"));
            await seed.SaveChangesAsync(Ct);
        }

        await using (var db = AnichronDbContexts.InMemory(name))
        {
            var repository = new EfIngestionFailureRepository(db);
            repository.Remove((await repository.GetByConfigAsync(config, Ct)).Single());
            repository.Add(Failure(config, "new.jpg"));
            await db.SaveChangesAsync(Ct);
        }

        await using var check = AnichronDbContexts.InMemory(name);
        (await check.IngestionFailures.Select(row => row.FilePath).ToListAsync(Ct)).Should().Equal("new.jpg");
    }
}
