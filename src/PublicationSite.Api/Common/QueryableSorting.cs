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
///
/// Every ordering ends in something unique. Nothing here is sorted by a column whose values are
/// all different: a date, a status, a role, a name. Rows that tie are left in whatever order the
/// database finds convenient, and it is under no obligation to find the same one twice. A page is
/// its own query, so two rows that tie across the boundary between page one and page two can both
/// come back on page one, and the reader never sees the other at all. It is the kind of fault
/// nobody reports, because the row that went missing is the one they did not know to look for.
/// </summary>
public static class QueryableSorting
{
    public static IOrderedQueryable<T> SortBy<T>(
        this IQueryable<T> query,
        PageRequest page,
        Expression<Func<T, object?>> fallback,
        Dictionary<string, Expression<Func<T, object?>>> columns,
        Expression<Func<T, object?>> tieBreaker,
        bool fallbackDescending = true)
    {
        if (page.SortBy is { Length: > 0 } requested
            && columns.TryGetValue(requested, out var column))
        {
            return (page.SortDescending
                    ? query.OrderByDescending(column)
                    : query.OrderBy(column))
                .ThenBy(tieBreaker);
        }

        return (fallbackDescending ? query.OrderByDescending(fallback) : query.OrderBy(fallback))
            .ThenBy(tieBreaker);
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
        Func<T, object?> tieBreaker,
        bool fallbackDescending = true)
    {
        if (page.SortBy is { Length: > 0 } requested
            && columns.TryGetValue(requested, out var column))
        {
            return (page.SortDescending
                    ? items.OrderByDescending(column)
                    : items.OrderBy(column))
                .ThenBy(tieBreaker);
        }

        return (fallbackDescending ? items.OrderByDescending(fallback) : items.OrderBy(fallback))
            .ThenBy(tieBreaker);
    }
}
