namespace PublicationSite.Api.Enums;

public enum EthicsStudentResponse
{
    Yes,
    No,
    Unsure
}

public enum EthicsStatus
{
    NotRequired,
    PendingUpload,
    PendingVerification,
    Verified
}

public enum EthicsDocumentType
{
    ApprovalCertificate,
    ApplicationForm,
    ParticipantConsentForm
}

public enum EthicsDocumentStatus
{
    PendingReview,
    Accepted,
    RevisionRequested
}
