using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Anichron.Core.Data.Repository;

/// <summary>
/// A memory as On This Day counts it: an asset outside a burst, or a burst as its cover, starred or liked when any
/// shown member is.
/// </summary>
public sealed record FlashbackItem
{
    /// <summary>The asset, or the burst's cover.</summary>
    public required Guid Id { get; init; }

    public required int Year { get; init; }

    public required int Month { get; init; }

    public required int Day { get; init; }

    public required LocalDateTime DateCaptured { get; init; }

    /// <summary>Whether the caller starred it, or any shown member of its burst.</summary>
    public required bool IsStarred { get; init; }

    /// <summary>Whether the caller liked it, or any shown member of its burst.</summary>
    public required bool IsLiked { get; init; }

    public required Guid? BurstId { get; init; }

    /// <summary>The burst's shown members, or 0 outside one.</summary>
    public required int BurstCount { get; init; }
}

public sealed record FlashbackBurst(Guid BurstId, int Count);

public sealed record FlashbackMemory(AssetView View, FlashbackBurst? Burst);

public sealed record FlashbackYear(int Year, IReadOnlyList<FlashbackMemory> Memories);

/// <summary>A day before the one asked for whose On This Day has memories, with its newest year's first memory.</summary>
/// <param name="Date">The day.</param>
/// <param name="Year">Its newest year.</param>
/// <param name="Memory">That year's first memory.</param>
public sealed record RecentDay(LocalDate Date, int Year, FlashbackMemory Memory);

public sealed record OnThisDay(LocalDate Date)
{
    internal int LastDay
        => Date is { Month: 2, Day: 28 } && !CalendarSystem.Iso.IsLeapYear(Date.Year) ? 29 : Date.Day;

    public Expression<Func<FlashbackItem, bool>> Includes()
    {
        var (year, month, firstDay, lastDay) = (Date.Year, Date.Month, Date.Day, LastDay);
        return item => item.Month == month && item.Day >= firstDay && item.Day <= lastDay && item.Year < year;
    }
}

/// <summary>The dates from <paramref name="Start"/> up to, not including, <paramref name="End"/>.</summary>
/// <param name="Start">The first date.</param>
/// <param name="End">The date after the last.</param>
internal sealed record DayWindow(LocalDate Start, LocalDate End)
{
    public static DayWindow Of(LocalDate date) => new(date, date.PlusDays(1));

    public bool IsEmpty => Start >= End;

    public IEnumerable<LocalDate> NewestFirst()
    {
        for (var date = End.PlusDays(-1); date >= Start; date = date.PlusDays(-1))
            yield return date;
    }

    public Expression<Func<MediaAsset, bool>> Covering()
    {
        var asset = Expression.Parameter(typeof(MediaAsset));
        var lastYear = End.PlusDays(-1).Year;
        Expression<Func<MediaAsset, bool>> earlier = a => a.Year < lastYear;
        var days = MonthRanges()
            .Select(range => Bind(DaysOf(range.Month, range.First, range.Last), asset))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<MediaAsset, bool>>(Expression.AndAlso(days, Bind(earlier, asset)), asset);
    }

    private static Expression<Func<MediaAsset, bool>> DaysOf(int month, int first, int last)
        => a => a.Month == month && a.Day >= first && a.Day <= last;

    private IEnumerable<(int Month, int First, int Last)> MonthRanges()
        => NewestFirst()
            .Select(date => (date.Month, date.Day, Last: new OnThisDay(date).LastDay))
            .GroupBy(day => day.Month)
            .Select(month => (month.Key, month.Min(day => day.Day), month.Max(day => day.Last)));

    private static Expression Bind(Expression<Func<MediaAsset, bool>> lambda, ParameterExpression asset)
        => Expression.Invoke(lambda, asset);
}

/// <summary>
/// Reads On This Day for the caller from narrow keys, and full views only for the memories it returns. Separate from
/// <see cref="IAssetViewReader"/>, so it cannot widen what the caller sees.
/// </summary>
public interface IFlashbackReader
{
    /// <summary>The day's memories, years newest first, each year starred first and then by capture time.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="date">The day.</param>
    /// <param name="ct">Cancels the query.</param>
    Task<IReadOnlyList<FlashbackYear>> ReadDayAsync(Guid userId, LocalDate date, CancellationToken ct);

    /// <summary>
    /// The recent days in <c>[max(since, before − 366), before)</c>, newest first: each day whose On This Day has
    /// memories, with its newest year and that year's first memory.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="before">The day after the newest day read.</param>
    /// <param name="since">The oldest day read, or <c>null</c> for a year back.</param>
    /// <param name="limit">The most days returned.</param>
    /// <param name="ct">Cancels the queries.</param>
    Task<IReadOnlyList<RecentDay>> ReadRecentDaysAsync(
        Guid userId, LocalDate before, LocalDate? since, int limit, CancellationToken ct);
}

public sealed class EfFlashbackReader(AnichronDbContext db) : IFlashbackReader
{
    public async Task<IReadOnlyList<FlashbackYear>> ReadDayAsync(Guid userId, LocalDate date, CancellationToken ct)
    {
        var items = await Items(db, userId, DayWindow.Of(date)).Where(new OnThisDay(date).Includes()).ToListAsync(ct);
        var memories = await MemoriesAsync(userId, InStoryOrder(items), ct);
        return [.. memories
            .GroupBy(memory => memory.View.Year)
            .Select(year => new FlashbackYear(year.Key, [.. year]))];
    }

    public async Task<IReadOnlyList<RecentDay>> ReadRecentDaysAsync(
        Guid userId, LocalDate before, LocalDate? since, int limit, CancellationToken ct)
    {
        var firsts = await WalkAsync(
            before, since, limit, async window => await Items(db, userId, window).ToListAsync(ct));
        var memories = (await MemoriesAsync(userId, firsts.Select(first => first.First).DistinctBy(item => item.Id), ct))
            .ToDictionary(memory => memory.View.Id);
        return [.. firsts.Join(
            memories,
            first => first.First.Id,
            memory => memory.Key,
            (first, memory) => new RecentDay(first.Date, first.First.Year, memory.Value))];
    }

    /// <summary>
    /// Walks back from the day before <paramref name="before"/>, reading the 31 days before it first and the rest of
    /// the window only when they hold fewer than <paramref name="limit"/> recent days.
    /// </summary>
    /// <param name="before">The day after the newest day read.</param>
    /// <param name="since">The oldest day read, or <c>null</c> for a year back.</param>
    /// <param name="limit">The most days returned.</param>
    /// <param name="read">Reads the items of a window.</param>
    internal static async Task<IReadOnlyList<(LocalDate Date, FlashbackItem First)>> WalkAsync(
        LocalDate before, LocalDate? since, int limit, Func<DayWindow, Task<IReadOnlyList<FlashbackItem>>> read)
    {
        var yearBack = before.PlusDays(-366);
        var start = LocalDate.Max(since ?? yearBack, yearBack);
        var split = LocalDate.Max(start, before.PlusDays(-31));
        var found = new List<(LocalDate, FlashbackItem)>();
        foreach (var window in new[] { new DayWindow(split, before), new DayWindow(start, split) })
        {
            if (found.Count == limit || window.IsEmpty)
                break;
            var byMonth = (await read(window)).ToLookup(item => item.Month);
            found.AddRange(window.NewestFirst()
                .Select(date => (date, First: InStoryOrder(byMonth[date.Month].Where(new OnThisDay(date).Includes().Compile())).FirstOrDefault()))
                .Where(day => day.First is not null)
                .Take(limit - found.Count)!);
        }

        return found;
    }

    private static IOrderedEnumerable<FlashbackItem> InStoryOrder(IEnumerable<FlashbackItem> items)
        => items
            .OrderByDescending(item => item.Year)
            .ThenByDescending(item => item.IsStarred)
            .ThenBy(item => item.DateCaptured)
            .ThenBy(item => item.Id);

    private async Task<IEnumerable<FlashbackMemory>> MemoriesAsync(
        Guid userId, IEnumerable<FlashbackItem> items, CancellationToken ct)
    {
        var wanted = items.ToList();
        var views = await EfAssetViewReader.Query(db, userId, new AssetsById([.. wanted.Select(item => item.Id)]))
            .ToListAsync(ct);
        return wanted.Join(
            views,
            item => item.Id,
            view => view.Id,
            (item, view) => new FlashbackMemory(
                view with { IsStarred = item.IsStarred, IsLiked = item.IsLiked },
                item.BurstId is { } burstId ? new FlashbackBurst(burstId, item.BurstCount) : null));
    }

    internal static IQueryable<FlashbackItem> Items(AnichronDbContext db, Guid userId, DayWindow window)
    {
        var shown = EfAssetViewReader.Listed(db, userId, AssetSet.Shown);
        var onTheDays = shown.Where(window.Covering());
        var burstIds = onTheDays.Where(a => a.BurstId != null).Select(a => a.BurstId);
        var members = Rows(db, userId, shown.Where(a => burstIds.Contains(a.BurstId)));
        var bursts = from m in members
                     group m by m.BurstId into burst
                     select new
                     {
                         BurstId = burst.Key,
                         Starred = burst.Max(m => m.IsStarred ? 1 : 0),
                         Liked = burst.Max(m => m.IsLiked ? 1 : 0),
                         Count = burst.Count(),
                     };
        var covers = from m in members
                     where !shown.Any(o => o.BurstId == m.BurstId
                         && (o.DateCaptured < m.DateCaptured || (o.DateCaptured == m.DateCaptured && o.Id.CompareTo(m.Id) < 0)))
                     join b in bursts on m.BurstId equals b.BurstId
                     select new FlashbackItem
                     {
                         Id = m.Id,
                         Year = m.Year,
                         Month = m.Month,
                         Day = m.Day,
                         DateCaptured = m.DateCaptured,
                         IsStarred = b.Starred == 1,
                         IsLiked = b.Liked == 1,
                         BurstId = m.BurstId,
                         BurstCount = b.Count,
                     };
        return Rows(db, userId, onTheDays.Where(a => a.BurstId == null)).Concat(covers);
    }

    private static IQueryable<FlashbackItem> Rows(AnichronDbContext db, Guid userId, IQueryable<MediaAsset> assets)
        => from a in assets
           join i in db.Interactions.Where(i => i.UserId == userId) on a.Id equals i.AssetId into found
           from i in found.DefaultIfEmpty()
           select new FlashbackItem
           {
               Id = a.Id,
               Year = a.Year!.Value,
               Month = a.Month!.Value,
               Day = a.Day!.Value,
               DateCaptured = a.DateCaptured!.Value,
               IsStarred = i != null && i.IsStarred,
               IsLiked = i != null && i.IsLiked,
               BurstId = a.BurstId,
               BurstCount = 0,
           };
}
