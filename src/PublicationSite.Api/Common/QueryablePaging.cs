using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Common;

/// <summary>
/// Turns a query into one page of it, counting the whole set in the database rather than fetching
/// it to count. Two round trips per screen regardless of how much there is.
/// </summary>
public static class QueryablePaging
{
    public static async Task<PagedResult<T>> ToPageAsync<T>(
        this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken = default)
    {
        var total = await query.CountAsync(cancellationToken);
        var number = PageWithin(page, total);

        var items = await query
            .Skip((number - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, number, page.SafePageSize, total);
    }

    /// <summary>
    /// Projects as it pages, so only the rows on the page are shaped into DTOs.
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPageAsync<TSource, TResult>(
        this IQueryable<TSource> query,
        System.Linq.Expressions.Expression<Func<TSource, TResult>> selector,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var total = await query.CountAsync(cancellationToken);
        var number = PageWithin(page, total);

        var items = await query
            .Skip((number - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, number, page.SafePageSize, total);
    }

    /// <summary>
    /// A page number that exists.
    ///
    /// Numbers below one were already brought back to the first page. Numbers past the end were
    /// not: asking for page forty of four came back as page forty, empty, with a total of thirty
    /// two beside it, and the pager drew "Page 40 of 4" over nothing. It happens on an ordinary
    /// path, not only from a hand-typed URL: somebody on the last page of a queue works through it,
    /// the rows they were reading leave, and the link they follow next names a page that no longer
    /// exists.
    ///
    /// The far end is brought back the same way the near end is, so what comes back is a page of
    /// rows and a number that agrees with the total beside it. Empty in the one case where that is
    /// the truth: nothing matched at all.
    /// </summary>
    private static int PageWithin(PageRequest page, int total)
    {
        if (total == 0) return 1;

        var last = (total + page.SafePageSize - 1) / page.SafePageSize;
        return Math.Min(page.SafePage, last);
    }
}
