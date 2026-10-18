using Anichron.Core.Data;
using Anichron.Testing.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Core.Tests.Unit.Data;

public sealed class UniqueViolationsTests
{
    [Fact]
    public void IsUniqueViolation_OfTheNamedIndex_IsTrue()
        => DatabaseErrors.UniqueViolation(MediaAssetIndexNames.FilePathUnique)
            .IsUniqueViolation(MediaAssetIndexNames.FilePathUnique).Should().BeTrue();

    [Fact]
    public void IsUniqueViolation_OfAnotherIndex_IsFalse()
        => DatabaseErrors.UniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique)
            .IsUniqueViolation(MediaAssetIndexNames.FilePathUnique).Should().BeFalse();

    [Fact]
    public void IsUniqueViolation_AnotherErrorOnTheNamedIndex_IsFalse()
        => DatabaseErrors.Violation(PostgresErrorCodes.ForeignKeyViolation, MediaAssetIndexNames.FilePathUnique)
            .IsUniqueViolation(MediaAssetIndexNames.FilePathUnique).Should().BeFalse();

    [Fact]
    public void IsUniqueViolation_WithoutAPostgresCause_IsFalse()
        => new DbUpdateException("boom", new InvalidOperationException())
            .IsUniqueViolation(MediaAssetIndexNames.FilePathUnique).Should().BeFalse();
}
