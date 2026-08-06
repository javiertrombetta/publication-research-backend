using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

/// <summary>
/// The hub entity that groups every artefact of a single student's publication process
/// (proposals, ethics workflow, research paper) across the three sequential pipelines.
/// </summary>
public class PublicationContainer : IHaveAConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentId { get; set; }
    public ApplicationUser Student { get; set; } = null!;

    public Guid CoordinatorId { get; set; }
    public ApplicationUser Coordinator { get; set; } = null!;

    public Guid? AssignedSupervisorId { get; set; }
    public ApplicationUser? AssignedSupervisor { get; set; }

    public PipelineStage CurrentPipeline { get; set; } = PipelineStage.ResearchProposals;
    public ContainerStatus Status { get; set; } = ContainerStatus.InProgress;

    /// <summary>
    /// The committee composition this publication will be judged by, copied from the system
    /// settings on the day it was opened.
    ///
    /// Snapshotted rather than read live because a publication runs for months: an administrator
    /// who decides in March that committees now need three external members must not thereby change
    /// the rules for research that has been under way since January. Null on containers created
    /// before this existed. Those fall back to whatever is configured now, which is the only figure
    /// anyone ever agreed for them.
    /// </summary>
    public int? RequiredReviewerMembers { get; set; }
    public int? RequiredExternalCommitteeMembers { get; set; }
    public int? RequiredCommitteeApprovals { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ResearchProposal> Proposals { get; set; } = [];
    public EthicsDeclaration? EthicsDeclaration { get; set; }
    public EthicsApproval? EthicsApproval { get; set; }
    public Publication? Publication { get; set; }
    public ICollection<ActivityHistoryEntry> ActivityHistory { get; set; } = [];

    /// <summary>
    /// Changed on every save, and part of the WHERE clause of every UPDATE. See
    /// <see cref="IHaveAConcurrencyStamp"/> for why a decision needs one.
    /// </summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
