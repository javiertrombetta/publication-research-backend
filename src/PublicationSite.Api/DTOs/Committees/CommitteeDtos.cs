namespace PublicationSite.Api.DTOs.Committees;

public record CommitteeMemberDto(
    Guid UserId,
    string Name,
    string RoleType,
    string Decision,
    string? DecisionComments,
    DateTime? DecidedAt);

/// <param name="Paper"> What the committee is being asked to judge, carried with the assignment.
/// Without it a member's list of assignments had to fetch each paper separately, a request per
/// committee, before the page could be shown at all. </param>
public record CommitteeDto(
    Guid Id,
    Guid PublicationId,
    CommitteePaperDto? Paper,
    string Status,
    int MinApprovalsRequired,
    IReadOnlyList<CommitteeMemberDto> Members,
    /// <summary>Whose paper it is, so a listing of committees can name the student.</summary>
    string? StudentName = null,
    /// <summary>
    /// Whether an administrator may still change it. A committee that has finished has produced
    /// the decisions the coordinator ruled on, and rearranging it afterwards would rewrite the
    /// record of a judgement already made.
    /// </summary>
    bool CanBeChanged = false);

/// <summary>
/// Changing a committee that is already sitting: who is on it, and how many of them have to
/// approve.
///
/// Separate from assigning one, and deliberately so. Assigning creates something that did not
/// exist; this alters something people are working on, so it always costs a reason and it is
/// refused once the committee has finished.
/// </summary>
/// <param name="MemberUserIds">The committee as it should now stand, not the people to add. Anyone left out is removed.</param>
/// <param name="Comments">Why. Required, and recorded on the publication's history.</param>
/// <param name="OverrideComposition">Set when the new shape departs from what this publication was opened under.</param>
public record UpdateCommitteeRequest(
    IReadOnlyList<Guid> MemberUserIds,
    int MinApprovalsRequired,
    string Comments,
    bool OverrideComposition = false);

/// <param name="OverrideComposition">
/// Set when this publication is to be judged by a committee of a different shape from the one it
/// was opened under. Asked for explicitly, and only accepted with a reason in Comments, because
/// the recorded composition is what the institution agreed for this piece of research: departing
/// from it is a decision somebody has to own and later readers have to be able to see. Nothing
/// already assigned is touched; this is settled before the committee exists.
/// </param>
public record AssignCommitteeRequest(
    IReadOnlyList<Guid> MemberUserIds, int MinApprovalsRequired, string Comments,
    bool OverrideComposition = false);

public record CommitteeMemberReviewRequest(bool Approve, string Comments);

public record CommitteeRoleConfigDto(Guid? CommitteeId, string RoleType, int RequiredCount);

public record SetCommitteeRoleConfigRequest(string RoleType, int RequiredCount);

/// <summary>
/// As much of a research paper as a committee member needs to see before opening it: enough to
/// tell their assignments apart and to judge what they are about.
/// </summary>
/// <param name="StudentName">Whose paper it is. The assignment queue lets a member search and order by the student, so a screen that never names one is offering controls over something it does not show. Nothing here is anonymous review: the same member can open the publication and see the author.</param>
public record CommitteePaperDto(
    Guid Id,
    string Title,
    string Abstract,
    int? PublicationYear,
    IReadOnlyList<string> Keywords,
    string? StudentName = null);

/// <summary>
/// Somebody who could be put on a committee. Their roles come with them so a screen can group or
/// explain the list without asking again, and <paramref name="IsExternal"/> because that is what
/// decides which of the two required counts they fill.
/// </summary>
public record CommitteeCandidateDto(
    Guid UserId,
    string FirstName,
    string LastName,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsExternal);
