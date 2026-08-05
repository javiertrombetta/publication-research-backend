using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.DTOs.Catalogue;

public record CatalogueEntryDto(
    Guid Id,
    string Title,
    string Abstract,
    string AuthorName,
    string? SupervisorName,
    IReadOnlyList<string> Keywords,
    string? PublicationType,
    int? PublicationYear,
    string? Department,
    IReadOnlyList<string> ResearchAreas);

public class CatalogueSearchRequest : Common.PageRequest
{
    /// <summary>
    /// A word to look for in the title or the abstract.
    /// </summary>
    public string? Query { get; set; }
    /// <summary>
    /// Part of the author's name.
    /// </summary>
    public string? Author { get; set; }
    /// <summary>
    /// Part of the supervisor's name.
    /// </summary>
    public string? Supervisor { get; set; }
    /// <summary>
    /// The year of publication, as the author recorded it.
    /// </summary>
    public int? Year { get; set; }
    /// <summary>
    /// One of the keywords attached to the paper. Matched whole rather than as a fragment.
    /// </summary>
    public string? Keyword { get; set; }
    /// <summary>
    /// The kind of work, as the author described it: a research paper, a thesis, and so on.
    /// </summary>
    public string? PublicationType { get; set; }
    /// <summary>
    /// The department the author belongs to, by name or by code.
    /// </summary>
    public string? Department { get; set; }
    /// <summary>
    /// One of the institution's research areas, by name.
    /// </summary>
    public string? ResearchArea { get; set; }
}


public record CitationDto(string Apa, string Mla);
