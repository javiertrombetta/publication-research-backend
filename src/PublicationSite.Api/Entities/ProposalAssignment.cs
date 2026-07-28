namespace PublicationSite.Api.Entities;

/// <summary>
/// The Coordinator's final allocation of a proposal to a Supervisor. One-to-one with
/// ResearchProposal: a student ends up with exactly one accepted proposal, or none.
/// </summary>
public class ProposalAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProposalId { get; set; }
    public ResearchProposal Proposal { get; set; } = null!;

    public Guid SupervisorId { get; set; }
    public ApplicationUser Supervisor { get; set; } = null!;

    public Guid CoordinatorId { get; set; }
    public ApplicationUser Coordinator { get; set; } = null!;

    public string Comments { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
