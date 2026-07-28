namespace PublicationSite.Api.DTOs.Committees;

public record CommitteeMemberDto(
    Guid UserId,
    string Name,
    string RoleType,
    string Decision,
    string? DecisionComments,
    DateTime? DecidedAt);

public record CommitteeDto(
    Guid Id,
    Guid PublicationId,
    string Status,
    int MinApprovalsRequired,
    IReadOnlyList<CommitteeMemberDto> Members);

public record AssignCommitteeRequest(IReadOnlyList<Guid> MemberUserIds, int MinApprovalsRequired, string Comments);

public record CommitteeMemberReviewRequest(bool Approve, string Comments);

public record CommitteeRoleConfigDto(Guid? CommitteeId, string RoleType, int RequiredCount);

public record SetCommitteeRoleConfigRequest(string RoleType, int RequiredCount);
