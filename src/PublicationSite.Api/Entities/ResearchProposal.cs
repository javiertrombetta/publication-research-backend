using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class ResearchProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Abstract { get; set; } = string.Empty;
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;

    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// When this proposal was last put back in the dispatch queue after a round that came to
    /// nothing, and null if it has never been. A proposal whose offers were discarded looks exactly
    /// like one that has never been sent, and the two are not the same thing: the second is waiting
    /// its turn, the first has already had one and needs different supervisors or a new proposal.
    /// Cleared when it goes out again.
    /// </summary>
    public DateTime? ReturnedToDispatchAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProposalSupervisorSelection> SupervisorSelections { get; set; } = [];
    public ProposalAssignment? Assignment { get; set; }
}
