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
/// publication and then a call for each of its proposals, so the page cost grew with the department
/// while showing the same handful of rows anyone could act on.
/// </summary>
/// <param name="ReturnedToDispatchAt">When this proposal was last put back in the dispatch queue after a round that found nobody, and null if it has never been. Lets a dispatch screen tell a proposal waiting its first turn from one that has already had one, which are different things to decide about.</param>
public record ProposalWithInvitationsDto(
    Guid Id,
    Guid PublicationContainerId,
    string StudentName,
    string Title,
    string Abstract,
    string Status,
    DateTime? SubmittedAt,
    IReadOnlyList<SupervisorInvitationDto> Invitations,
    DateTime? ReturnedToDispatchAt = null);

/// <summary>
/// What discarding a set of offers actually did. The coordinator refused the offers on one
/// proposal, and whether that took the whole student back to the dispatch queue depends on what
/// else they had, so the answer comes back rather than being guessed at by the caller.
/// </summary>
/// <param name="StudentHasNothingLeft">True when no proposal of theirs still has a supervisor willing to take it on, so every one of them went back.</param>
public record DiscardSelectionsResultDto(
    string StudentName,
    int ProposalsReturned,
    bool StudentHasNothingLeft);

/// <summary>
/// How much of the dispatch queue is there for the second time. Counted over the whole queue
/// rather than the page in hand, because it is a figure a coordinator reads to decide what to do
/// next and a page is not the queue.
/// </summary>
public record ReturnedToDispatchSummaryDto(int Students, int Proposals);

