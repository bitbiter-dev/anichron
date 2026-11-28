namespace Anichron.Core.Data.Repository;

/// <summary>The last row of a page ordered by a time, newest first, then by id descending.</summary>
/// <param name="At">The row's time in that order.</param>
/// <param name="AssetId">The row's id.</param>
public sealed record AssetCursor(Instant At, Guid AssetId);
