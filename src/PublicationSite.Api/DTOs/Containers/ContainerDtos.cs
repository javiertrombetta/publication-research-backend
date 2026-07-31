namespace PublicationSite.Api.DTOs.Containers;

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
    /// <summary>
    /// Best available label for this container: the research paper's title once one exists,
    /// otherwise the approved proposal's title. Null while the student is still drafting
    /// proposals. Needed because a student can have several containers at once, and the
    /// container itself carries no name of its own.
    /// </summary>
    string? Title,
    /// <summary>
    /// How many research proposals this Container holds. Zero means it is still empty, which is
    /// the only point at which its owning student may discard it.
    /// </summary>
    int ProposalCount,
    /// <summary>
    /// The research paper's own status, or null while no paper exists yet. The Container's
    /// Status only distinguishes InProgress from Completed, so without this a listing cannot
    /// tell an accepted paper awaiting its publication decision from one still being reviewed.
    /// </summary>
    string? PaperStatus = null);

public record ActivityHistoryEntryDto(
    Guid Id,
    string ActorName,
    /// <summary>
    /// The capacity the actor was acting in (Coordinator, Supervisor, ...). Without it the
    /// history is a list of names, and a student reading it can't tell who decided what.
    /// Null only if the account somehow carries no role.
    /// </summary>
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
