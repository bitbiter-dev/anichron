namespace Anichron.Core.Data.Repository;

/// <summary>A page of rows in an <see cref="AssetCursor"/>'s order.</summary>
/// <typeparam name="T">The row.</typeparam>
/// <param name="Items">The page's rows.</param>
/// <param name="Next">The cursor of the next page, or <c>null</c> on the last page.</param>
public sealed record AssetPage<T>(IReadOnlyList<T> Items, AssetCursor? Next);

public static class AssetPage
{
    /// <summary>Reads one row more than the page holds, and points the next page at the page's last row when it came.</summary>
    /// <typeparam name="T">The row.</typeparam>
    /// <param name="size">The most rows on the page.</param>
    /// <param name="read">Reads at most the given count of rows after the previous page.</param>
    /// <param name="cursorOf">The cursor a row ends its page with.</param>
    public static async Task<AssetPage<T>> ReadAsync<T>(
        int size, Func<int, Task<IReadOnlyList<T>>> read, Func<T, AssetCursor> cursorOf)
    {
        var rows = await read(size + 1);
        var items = rows.Take(size).ToList();
        return new AssetPage<T>(items, rows.Count > size ? cursorOf(items[^1]) : null);
    }
}
