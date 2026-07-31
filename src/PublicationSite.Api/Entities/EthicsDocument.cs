using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class EthicsDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EthicsApprovalId { get; set; }
    public EthicsApproval EthicsApproval { get; set; } = null!;

    /// <summary>Which of the required documents this file is. Was a fixed enum; now configurable data.</summary>
    public Guid EthicsDocumentRequirementId { get; set; }
    public EthicsDocumentRequirement EthicsDocumentRequirement { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    public Guid UploadedByUserId { get; set; }
    public ApplicationUser UploadedByUser { get; set; } = null!;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public EthicsDocumentStatus Status { get; set; } = EthicsDocumentStatus.PendingReview;
    public string? ReviewComments { get; set; }
}
