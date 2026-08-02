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
    IReadOnlyList<CommitteeMemberDto> Members);

public record AssignCommitteeRequest(IReadOnlyList<Guid> MemberUserIds, int MinApprovalsRequired, string Comments);

public record CommitteeMemberReviewRequest(bool Approve, string Comments);

public record CommitteeRoleConfigDto(Guid? CommitteeId, string RoleType, int RequiredCount);

public record SetCommitteeRoleConfigRequest(string RoleType, int RequiredCount);

/// <summary>
/// As much of a research paper as a committee member needs to see before opening it: enough to
/// tell their assignments apart and to judge what they are about.
/// </summary>
public record CommitteePaperDto(
    Guid Id,
    string Title,
    string Abstract,
    int? PublicationYear,
    IReadOnlyList<string> Keywords);

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
