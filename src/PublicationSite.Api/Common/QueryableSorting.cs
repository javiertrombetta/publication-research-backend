using System.Linq.Expressions;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Common;

/// <summary>
/// Applies the caller's chosen ordering before the page is cut.
///
/// Each endpoint declares the columns it can be ordered by, as a map from the name a client sends
/// to the expression the database orders on. Nothing is built from a string at run time, so a sort
/// key cannot reach the database as SQL, and a column that is not on the list simply is not one
/// this endpoint sorts by.
///
/// An unrecognised key falls back to the endpoint's default order rather than failing. A sort is a
/// view preference: refusing to return the page because somebody's bookmark names a column that
/// has since been renamed would be punishing the reader for the rename.
/// </summary>
public static class QueryableSorting
{
    public static IOrderedQueryable<T> SortBy<T>(
        this IQueryable<T> query,
        PageRequest page,
        Expression<Func<T, object?>> fallback,
        Dictionary<string, Expression<Func<T, object?>>> columns,
        bool fallbackDescending = true)
    {
        if (page.SortBy is { Length: > 0 } requested
            && columns.TryGetValue(requested, out var column))
        {
            return page.SortDescending
                ? query.OrderByDescending(column)
                : query.OrderBy(column);
        }

        return fallbackDescending ? query.OrderByDescending(fallback) : query.OrderBy(fallback);
    }

    /// <summary>
    /// The same, for a list already in memory. Used where the rows a screen shows are assembled
    /// from more than one source and cannot be ordered in SQL.
    /// </summary>
    public static IOrderedEnumerable<T> SortBy<T>(
        this IEnumerable<T> items,
        PageRequest page,
        Func<T, object?> fallback,
        Dictionary<string, Func<T, object?>> columns,
        bool fallbackDescending = true)
    {
        if (page.SortBy is { Length: > 0 } requested
            && columns.TryGetValue(requested, out var column))
        {
            return page.SortDescending
                ? items.OrderByDescending(column)
                : items.OrderBy(column);
        }

        return fallbackDescending ? items.OrderByDescending(fallback) : items.OrderBy(fallback);
    }
}
