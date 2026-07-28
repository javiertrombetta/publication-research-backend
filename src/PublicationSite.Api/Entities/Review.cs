using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationVersionId { get; set; }
    public PublicationVersion PublicationVersion { get; set; } = null!;

    public Guid ReviewerUserId { get; set; }
    public ApplicationUser ReviewerUser { get; set; } = null!;
    public ReviewerType ReviewerType { get; set; }

    public ReviewDecision Decision { get; set; }
    public string Comments { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
}
