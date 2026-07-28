namespace PublicationSite.Api.Entities;

/// <summary>
/// One row per (proposal, invited supervisor). Created when the Coordinator sends a
/// proposal to a Supervisor for consideration (IsSelected = false); updated when the
/// Supervisor marks it as one they would be willing to supervise. The Coordinator's
/// final allocation may only pick among rows where IsSelected is true.
/// </summary>
public class ProposalSupervisorSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProposalId { get; set; }
    public ResearchProposal Proposal { get; set; } = null!;

    public Guid SupervisorId { get; set; }
    public ApplicationUser Supervisor { get; set; } = null!;

    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    public bool IsSelected { get; set; }
    public string? Comments { get; set; }
    public DateTime? SelectedAt { get; set; }
}
