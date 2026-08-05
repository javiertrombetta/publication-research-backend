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
    /// The last resort, for when nothing has said otherwise: how long a page is belongs to the
    /// institution, and is read from settings by ConfiguredPageSizeFilter before an action runs.
    /// This is what stands in if that setting has never been written.
    /// </summary>
    public const int DefaultPageSize = 10;

    /// <summary>
    /// A ceiling, whatever anybody asks for. Without one, a single request could be made to load
    /// the whole institution and undo the point of paging altogether.
    /// </summary>
    public const int MaximumPageSize = 100;

    /// <summary>
    /// Which page to return, counted from one. Out-of-range numbers are clamped rather than
    /// refused: they arrive from stale links as often as from a working client.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Zero means "however long the institution says", which is the usual case: the site sends no
    /// page size at all and takes the configured one. A caller that names a number gets it, up to
    /// the ceiling above.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Which column to order by, and whether to reverse it.
    ///
    /// It belongs here, on the request, rather than in the client. Sorting the ten rows a page
    /// happens to hold is not sorting the list: the oldest proposal in the department is on the
    /// last page, and a reader clicking "oldest first" expects to see it, not the oldest of
    /// whatever ten they were already looking at. Ordering has to happen before the page is cut.
    ///
    /// The name is whatever the endpoint documents; an unknown one falls back to that endpoint's
    /// own default rather than failing, because a sort key is a view preference and not worth
    /// refusing a page over.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Reverses <see cref="SortBy"/>. Ignored where no column has been named, since there is
    /// nothing to reverse but the endpoint's own default order.
    /// </summary>
    public bool SortDescending { get; set; }

    /// <summary>
    /// Clamped rather than rejected. These numbers arrive from stale links and typed addresses as
    /// often as from a working client, and the nearest sensible page is a better answer than an
    /// error for something nobody meant to get wrong.
    ///
    /// Hidden from binding, and so from the API reference: they are what the two above are read as,
    /// not two more knobs. Swagger listed all four and offered a caller a choice that does not
    /// exist. Nothing is bound to these, since they have no setter.
    /// </summary>
    [BindNever]
    public int SafePage => Math.Max(1, Page);

    /// <inheritdoc cref="SafePage"/>
    [BindNever]
    public int SafePageSize => Math.Clamp(PageSize <= 0 ? DefaultPageSize : PageSize, 1, MaximumPageSize);
}
