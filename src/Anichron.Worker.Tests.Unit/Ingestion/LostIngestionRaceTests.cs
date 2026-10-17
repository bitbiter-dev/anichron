using Anichron.Core.Data;
using Anichron.Worker.Ingestion;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Worker.Tests.Unit.Ingestion;

public sealed class LostIngestionRaceTests
{
    private static DbUpdateException Violation(string sqlState, string constraintName)
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", sqlState,
            null, null, 0, 0, null, null, null, null, null, null,
            constraintName, null, null, null));

    [Theory]
    [InlineData(MediaAssetIndexNames.ActiveContentHashUnique)]
    [InlineData(ProxyFileIndexNames.OnePerAssetAndType)]
    public void Matches_AUniqueViolationOfAContentDerivedIndex(string constraintName)
        => LostIngestionRace.Matches(Violation(PostgresErrorCodes.UniqueViolation, constraintName)).Should().BeTrue();

    [Fact]
    public void Matches_NotAUniqueViolationOfAnotherIndex()
        => LostIngestionRace.Matches(Violation(PostgresErrorCodes.UniqueViolation, "IX_MediaAssets_StorageConfigId_FilePath"))
            .Should().BeFalse();

    [Fact]
    public void Matches_NotAnotherErrorOnTheSameIndex()
        => LostIngestionRace.Matches(Violation(PostgresErrorCodes.ForeignKeyViolation, ProxyFileIndexNames.OnePerAssetAndType))
            .Should().BeFalse();

    [Fact]
    public void Matches_NotAFailureWithoutAPostgresCause()
        => LostIngestionRace.Matches(new DbUpdateException("boom", new InvalidOperationException()))
            .Should().BeFalse();
}
