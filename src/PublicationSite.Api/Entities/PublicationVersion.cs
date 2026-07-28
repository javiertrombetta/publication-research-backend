namespace PublicationSite.Api.Entities;

public class PublicationVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationId { get; set; }
    public Publication Publication { get; set; } = null!;

    public int VersionNumber { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? SupplementaryFilesPath { get; set; }
    public string? ReviewerNotes { get; set; }

    public Guid UploadedByUserId { get; set; }
    public ApplicationUser UploadedByUser { get; set; } = null!;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Review> Reviews { get; set; } = [];
}
