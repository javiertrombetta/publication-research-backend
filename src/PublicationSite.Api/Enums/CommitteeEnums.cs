namespace PublicationSite.Api.Enums;

/// <summary>
/// What a committee seat is filled by. A Reviewer is one of this institution's own, an External is
/// a member of staff from somewhere else, and the difference is what committee composition counts.
/// </summary>
public enum CommitteeMemberRoleType
{
    Reviewer,
    External
}

public enum CommitteeMemberDecision
{
    Pending,
    Approve,
    Reject
}

public enum CommitteeStatus
{
    Assigned,
    InReview,
    Completed
}
