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

    /// <summary>
    /// When the Coordinator asked for an answer by, and null where they set no date. Held on the
    /// invitation rather than the proposal because it belongs to the round: the same proposal sent
    /// again after a round that found nobody gets a new date, and the old one should not follow it.
    /// </summary>
    public DateTime? RespondBy { get; set; }

    /// <summary>
    /// When this supervisor was warned their answer-by date was close, so they are warned once
    /// rather than on every sweep. An invitation is thrown away when its round expires, so a fresh
    /// round warns again without anything having to reset this.
    /// </summary>
    public DateTime? DueSoonWarnedAt { get; set; }

    public bool IsSelected { get; set; }
    public string? Comments { get; set; }
    public DateTime? SelectedAt { get; set; }
}
