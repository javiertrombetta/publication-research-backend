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

public class CatalogueSearchRequest
{
    public string? Query { get; set; }
    public string? Author { get; set; }
    public string? Supervisor { get; set; }
    public int? Year { get; set; }
    public string? Keyword { get; set; }
    public string? PublicationType { get; set; }
    public string? Department { get; set; }
    public string? ResearchArea { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record CitationDto(string Apa, string Mla);
