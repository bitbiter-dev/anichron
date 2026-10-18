using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Core.Data;

public static class UniqueViolations
{
    public static bool IsUniqueViolation(this DbUpdateException ex, string indexName)
    {
        var postgres = ex.InnerException as PostgresException;
        return postgres?.SqlState == PostgresErrorCodes.UniqueViolation && postgres.ConstraintName == indexName;
    }
}
