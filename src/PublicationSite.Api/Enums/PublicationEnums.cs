namespace PublicationSite.Api.Enums;

public enum PublicationStatus
{
    Draft,
    Submitted,
    EthicsVerification,
    UnderReview,
    RevisionsRequested,
    Resubmitted,
    Accepted,
    Published
}

public enum ReviewerType
{
    Supervisor,
    CommitteeMember
}

public enum ReviewDecision
{
    Approve,
    Reject,
    RequestRevision
}
