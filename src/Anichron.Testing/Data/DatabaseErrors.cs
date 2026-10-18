using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Testing.Data;

public static class DatabaseErrors
{
    public static DbUpdateException UniqueViolation(string constraintName)
        => Violation(PostgresErrorCodes.UniqueViolation, constraintName);

    public static DbUpdateException Violation(string sqlState, string constraintName)
        => new("duplicate key", new PostgresException(
            "duplicate key", "ERROR", "ERROR", sqlState,
            null, null, 0, 0, null, null, null, null, null, null,
            constraintName, null, null, null));
}
