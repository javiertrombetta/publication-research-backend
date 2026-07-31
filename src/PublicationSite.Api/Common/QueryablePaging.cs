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
        var items = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page.SafePage, page.SafePageSize, total);
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
        var items = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, page.SafePage, page.SafePageSize, total);
    }
}
