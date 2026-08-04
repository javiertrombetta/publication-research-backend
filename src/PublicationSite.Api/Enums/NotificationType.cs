namespace PublicationSite.Api.Enums;

public enum NotificationType
{
    ProposalsAwaitingEvaluation,
    ProposalAccepted,
    ProposalNotAccepted,
    NewProposalSubmissionRequested,
    EthicsEvaluationRequested,
    EthicsDocumentationRequired,
    EthicsDocumentationReadyForReview,
    EthicsRevisionRequested,
    EthicsCoordinatorReviewRequested,
    EthicsHeadOfDepartmentReviewRequested,
    EthicsFinalDecisionRequested,
    EthicsApprovalCompleted,
    ResearchPaperRevisionRequested,
    CommitteeReviewRequested,
    CommitteeFinalReviewRequested,
    PublicationApproved,
    PublicationDecisionRequested,

    /// <summary>An administrator has made somebody responsible for a publication already under way.</summary>
    ContainerAssigned,

    Generic
}
