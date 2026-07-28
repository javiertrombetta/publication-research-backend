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
