namespace PublicationSite.Api.DTOs.Proposals;

/// <param name="RespondBy">When the supervisor reading this has to have answered by. Only filled in on the listing of proposals sent to a supervisor, because that is the only place anybody is being held to it; null everywhere else, and null there too where the coordinator set no date.</param>
public record ProposalDto(
    Guid Id,
    Guid PublicationContainerId,
    string Title,
    string Abstract,
    string Status,
    DateTime? SubmittedAt,
    DateTime? RespondBy = null);

public record SaveProposalRequest(string Title, string Abstract);

/// <param name="RespondBy">When the supervisors have to answer by, or null for no date. Once it passes, students with no proposal anybody offered to take on go back to the dispatch queue on their own.</param>
public record SendToSupervisorsRequest(
    IReadOnlyList<Guid> ProposalIds,
    IReadOnlyList<Guid> SupervisorIds,
    string Comments,
    DateTime? RespondBy = null);

public record SupervisorSelectionRequest(string? Comments);

public record AssignSupervisorRequest(Guid SupervisorId, string Comments);

public record SupervisorInvitationDto(
    Guid ProposalId,
    Guid SupervisorId,
    string SupervisorName,
    bool IsSelected,
    string? Comments,
    DateTime InvitedAt,
    DateTime? SelectedAt,
    DateTime? RespondBy = null);

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
/// What discarding a set of offers actually did. Whether it emptied the student's round depends on
/// what else they had, so the answer comes back rather than being guessed at by the caller.
/// </summary>
/// <param name="ProposalsReturned">How many went back to the dispatch queue. Zero unless the round came to nothing, because a proposal turned down while others are still live goes nowhere.</param>
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

