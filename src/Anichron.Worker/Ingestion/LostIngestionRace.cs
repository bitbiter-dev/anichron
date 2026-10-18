using Anichron.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Worker.Ingestion;

internal static class LostIngestionRace
{
    internal static bool Matches(DbUpdateException ex)
        => ex.IsUniqueViolation(MediaAssetIndexNames.ActiveContentHashUnique)
            || ex.IsUniqueViolation(ProxyFileIndexNames.OnePerAssetAndType);
}
