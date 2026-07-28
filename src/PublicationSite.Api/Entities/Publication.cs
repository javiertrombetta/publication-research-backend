using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

/// <summary>
/// The research paper itself. One-to-one with PublicationContainer; revisions are
/// tracked via PublicationVersion rather than by creating new Publication rows.
/// </summary>
public class Publication
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Abstract { get; set; } = string.Empty;
    public string? PublicationType { get; set; }
    public int? PublicationYear { get; set; }

    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;

    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public ApplicationUser? PublishedByUser { get; set; }

    public Guid? PublicationCategoryId { get; set; }
    public PublicationCategory? PublicationCategory { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Keyword> Keywords { get; set; } = [];
    public ICollection<ResearchArea> ResearchAreas { get; set; } = [];
    public ICollection<PublicationVersion> Versions { get; set; } = [];
    public Committee? Committee { get; set; }
}
