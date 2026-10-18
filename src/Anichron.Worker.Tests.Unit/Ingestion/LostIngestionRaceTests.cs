using Anichron.Core.Data;
using Anichron.Testing.Data;
using Anichron.Worker.Ingestion;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class LostIngestionRaceTests
{
    [Theory]
    [InlineData(MediaAssetIndexNames.ActiveContentHashUnique)]
    [InlineData(ProxyFileIndexNames.OnePerAssetAndType)]
    public void Matches_AUniqueViolationOfAContentDerivedIndex(string constraintName)
        => LostIngestionRace.Matches(DatabaseErrors.Violation(PostgresErrorCodes.UniqueViolation, constraintName)).Should().BeTrue();

    [Fact]
    public void Matches_NotAUniqueViolationOfAnotherIndex()
        => LostIngestionRace.Matches(DatabaseErrors.Violation(PostgresErrorCodes.UniqueViolation, "IX_MediaAssets_StorageConfigId_FilePath"))
            .Should().BeFalse();

    [Fact]
    public void Matches_NotAnotherErrorOnTheSameIndex()
        => LostIngestionRace.Matches(DatabaseErrors.Violation(PostgresErrorCodes.ForeignKeyViolation, ProxyFileIndexNames.OnePerAssetAndType))
            .Should().BeFalse();

    [Fact]
    public void Matches_NotAFailureWithoutAPostgresCause()
        => LostIngestionRace.Matches(new DbUpdateException("boom", new InvalidOperationException()))
            .Should().BeFalse();
}
