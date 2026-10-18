using Anichron.Testing.Data;
using Npgsql;

namespace Anichron.Testing.Tests.Unit.Data;

public sealed class DatabaseErrorsTests
{
    [Fact]
    public void UniqueViolation_CarriesAPostgresUniqueViolationOnTheNamedConstraint()
        => DatabaseErrors.UniqueViolation("ix_some_index").InnerException.Should().BeOfType<PostgresException>()
            .Which.Should().Match<PostgresException>(e =>
                e.SqlState == PostgresErrorCodes.UniqueViolation && e.ConstraintName == "ix_some_index");

    [Fact]
    public void Violation_CarriesTheGivenSqlState()
        => DatabaseErrors.Violation(PostgresErrorCodes.ForeignKeyViolation, "fk_some").InnerException
            .Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
}
