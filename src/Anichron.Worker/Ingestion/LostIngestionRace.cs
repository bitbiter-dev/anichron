using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anichron.Worker.Ingestion;

internal static class LostIngestionRace
{
    internal static bool Matches(DbUpdateException ex)
        => ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: MediaAssetIndexNames.ActiveContentHashUnique or ProxyFileIndexNames.OnePerAssetAndType,
        };
}
