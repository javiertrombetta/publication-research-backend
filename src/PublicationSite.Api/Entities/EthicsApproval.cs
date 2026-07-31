using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class EthicsApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public EthicsStatus Status { get; set; } = EthicsStatus.PendingSupervisorDecision;

    public string? ReferenceNumber { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public bool? IsRequiredPerSupervisor { get; set; }
    public string? SupervisorDecisionComments { get; set; }
    public DateTime? SupervisorDecisionAt { get; set; }

    public bool? IsRequiredPerCoordinator { get; set; }
    public string? CoordinatorDecisionComments { get; set; }
    public DateTime? CoordinatorDecisionAt { get; set; }

    public string? HeadOfDepartmentComments { get; set; }
    public DateTime? HeadOfDepartmentReviewedAt { get; set; }

    public DateTime? FinalDecisionAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EthicsDocument> Documents { get; set; } = [];
}
