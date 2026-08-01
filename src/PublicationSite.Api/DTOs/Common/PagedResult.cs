using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PublicationSite.Api.DTOs.Common;

/// <summary>
/// One page of a longer list, with what a caller needs to ask for the next.
///
/// Lives here rather than beside the catalogue that first needed it: every queue in the system
/// returns one now, so a screen never receives a list whose length is decided by the size of a
/// department.
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>
/// How much of a list a caller wants. Bound from the query string, so a page is a link.
/// </summary>
public class PageRequest
{
    /// <summary>
    /// Ten rows, matching what the screens show. A caller can ask for more, up to a ceiling —
    /// without one, a single request could be made to load the whole institution and undo the
    /// point of paging at all.
    /// </summary>
    public const int DefaultPageSize = 10;
    public const int MaximumPageSize = 100;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Clamped rather than rejected. These numbers arrive from stale links and typed addresses as
    /// often as from a working client, and the nearest sensible page is a better answer than an
    /// error for something nobody meant to get wrong.
    ///
    /// Hidden from binding, and so from the API reference: they are what the two above are read as,
    /// not two more knobs. Swagger listed all four and offered a caller a choice that does not
    /// exist — nothing is bound to these, since they have no setter.
    /// </summary>
    [BindNever]
    public int SafePage => Math.Max(1, Page);

    /// <inheritdoc cref="SafePage"/>
    [BindNever]
    public int SafePageSize => Math.Clamp(PageSize <= 0 ? DefaultPageSize : PageSize, 1, MaximumPageSize);
}
