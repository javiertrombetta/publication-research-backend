namespace PublicationSite.Api.Enums;

public enum EthicsStudentResponse
{
    Yes,
    No,
    Unsure
}

/// <summary>
/// Persisted as an int, so the members keep their existing values and anything new is appended.
/// </summary>
public enum EthicsStatus
{
    /// <summary>A Supervisor has decided no ethics documentation is needed.</summary>
    NotRequired = 0,
    PendingUpload = 1,
    PendingVerification = 2,
    Verified = 3,

    /// <summary>
    /// The student has made their declaration and nobody has ruled on it yet. This is the state
    /// an approval starts in: without it, a brand-new approval defaulted to NotRequired (the
    /// enum's zero) and so claimed a decision that no Supervisor had actually made.
    /// </summary>
    PendingSupervisorDecision = 4
}

public enum EthicsDocumentStatus
{
    PendingReview,
    Accepted,
    RevisionRequested
}
