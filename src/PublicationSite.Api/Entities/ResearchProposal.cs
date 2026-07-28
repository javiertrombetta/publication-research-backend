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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProposalSupervisorSelection> SupervisorSelections { get; set; } = [];
    public ProposalAssignment? Assignment { get; set; }
}
