using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.DTOs.Containers;

/// <param name="Title">Best available label for this container: the research paper's title once one exists, otherwise the approved proposal's title. Null while the student is still drafting proposals. Needed because a student can have several containers at once, and the container itself carries no name of its own.</param>
/// <param name="ProposalCount">How many research proposals this Container holds. Zero means it is still empty, which is the only point at which its owning student may discard it.</param>
/// <param name="PaperStatus">The research paper's own status, or null while no paper exists yet. The Container's Status only distinguishes InProgress from Completed, so without this a listing cannot tell an accepted paper awaiting its publication decision from one still being reviewed.</param>
/// <param name="EthicsStatus">The ethics approval's status, or null before the student has declared. Lets a listing show what a Container is waiting on without a request per row.</param>
/// <param name="EthicsAwaitingRole">Whose turn it is in the ethics workflow, as a role name, or null when nothing is pending. EthicsStatus alone cannot answer this. PendingVerification covers four different waits — the Supervisor checking the documents, the Coordinator reviewing them, the Head of Department commenting, and the Coordinator's final decision — told apart only by which timestamps have been set. Deriving that belongs here rather than in every client.</param>
/// <param name="PaperAwaitingRole">Whose turn it is on the research paper, as a role name, or null when nothing is pending — before a paper exists, and once it is published. UnderReview alone cannot answer this: it covers the Supervisor still reading the paper, an Admin appointing a committee, the committee voting and the Coordinator's decision, told apart only by what has been recorded against it. RoleNames.EvaluationCommittee is returned where the wait belongs to the committee as a body rather than to one role.</param>
/// <param name="RequiredInternalCommitteeMembers">The evaluation committee this publication needs, as agreed when it was opened rather than as configured today — so an administrator assigning a committee months later is told the figures this piece of research was actually started under. Null on containers created before the figures were recorded; the current settings apply to those.</param>
public record PublicationContainerDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    Guid CoordinatorId,
    string CoordinatorName,
    Guid? AssignedSupervisorId,
    string? AssignedSupervisorName,
    int CurrentPipeline,
    string Status,
    DateTime CreatedAt,
    string? Title,
    int ProposalCount,
    string? PaperStatus = null,
    string? EthicsStatus = null,
    string? EthicsAwaitingRole = null,
    string? PaperAwaitingRole = null,
    /// <summary>
    /// Which ethics decision this is waiting for, by name. See Common/EthicsSteps.
    /// </summary>
    string? EthicsAwaitingStep = null,
    int? RequiredInternalCommitteeMembers = null,
    int? RequiredExternalCommitteeMembers = null);

/// <param name="ActorRole">The capacity the actor was acting in (Coordinator, Supervisor, ...). Without it the history is a list of names, and a student reading it can't tell who decided what. Null only if the account somehow carries no role.</param>
public record ActivityHistoryEntryDto(
    Guid Id,
    string ActorName,
    string? ActorRole,
    string? OnBehalfOfName,
    string Action,
    string Comments,
    string? PreviousStatus,
    string? NewStatus,
    DateTime CreatedAt);

/// <summary>
/// Admin manual assignment. <paramref name="PublicationContainerId"/> selects which of the
/// student's containers to reassign; omit it to create an additional container for them.
/// </summary>
public record AssignCoordinatorRequest(
    Guid StudentUserId,
    Guid CoordinatorUserId,
    string Comments,
    Guid? PublicationContainerId = null);

/// <summary>
/// How much of the container list a caller wants, and which of it.
///
/// EthicsStep is what lets a screen ask for its own queue instead of everything: the screens used
/// to fetch every container and filter in the browser, which meant no page could be a page of one
/// screen. Comma-separated, because the Coordinator's first ethics screen covers two steps that
/// arrive at the same moment.
/// </summary>
public class ContainerQuery : PageRequest
{
    public Guid? StudentId { get; set; }
    public Guid? CoordinatorId { get; set; }
    public string? Status { get; set; }

    /// <summary>One or more names from Common/EthicsSteps, comma-separated.</summary>
    public string? EthicsSteps { get; set; }

    public IReadOnlyList<string>? EthicsStep => string.IsNullOrWhiteSpace(EthicsSteps)
        ? null
        : EthicsSteps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
