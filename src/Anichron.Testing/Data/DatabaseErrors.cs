using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Testing.Data;

public static class DatabaseErrors
{
    public static DbUpdateException UniqueViolation(string? constraintName = null)
        => Violation(PostgresErrorCodes.UniqueViolation, constraintName);

    // PostgresException.ConstraintName has no setter, so only the full constructor can set it.
    public static DbUpdateException Violation(string sqlState, string? constraintName = null)
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", sqlState,
            null, null, 0, 0, null, null, null, null, null, null,
            constraintName, null, null, null));
}
