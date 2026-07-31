namespace PublicationSite.Api.DTOs.Proposals;

public record ProposalDto(
    Guid Id,
    Guid PublicationContainerId,
    string Title,
    string Abstract,
    string Status,
    DateTime? SubmittedAt);

public record SaveProposalRequest(string Title, string Abstract);

public record SendToSupervisorsRequest(IReadOnlyList<Guid> ProposalIds, IReadOnlyList<Guid> SupervisorIds, string Comments);

public record SupervisorSelectionRequest(string? Comments);

public record AssignSupervisorRequest(Guid SupervisorId, string Comments);

public record SupervisorInvitationDto(
    Guid ProposalId,
    Guid SupervisorId,
    string SupervisorName,
    bool IsSelected,
    string? Comments,
    DateTime InvitedAt,
    DateTime? SelectedAt);

/// <summary>
/// A research proposal together with the Supervisors it was sent to and what they said.
///
/// Exists so an overview screen can be one request instead of one per proposal. Building the
/// coordinator's supervisor-selection page from the per-container endpoints meant a call for each
/// publication and then a call for each of its proposals — the page cost grew with the department
/// while showing the same handful of rows anyone could act on.
/// </summary>
public record ProposalWithInvitationsDto(
    Guid Id,
    Guid PublicationContainerId,
    string StudentName,
    string Title,
    string Abstract,
    string Status,
    DateTime? SubmittedAt,
    IReadOnlyList<SupervisorInvitationDto> Invitations);

