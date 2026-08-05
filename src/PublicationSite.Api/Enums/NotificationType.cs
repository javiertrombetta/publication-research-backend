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

    Generic,

    // Appended after Generic on purpose. These are stored as ints, and putting anything in the
    // middle would renumber every member below it, so notifications already sent would come back
    // meaning something else. New kinds go on the end from here on.

    /// <summary>An answer-by date is close and the supervisor has not said which proposals they would take on.</summary>
    ProposalResponseDueSoon,

    /// <summary>An ethics review has passed the time allowed for it. Goes to the coordinator.</summary>
    EthicsReviewOverdue,

    /// <summary>An ethics review is close to the time allowed for it. Goes to whoever owes it.</summary>
    EthicsReviewDueSoon,

    /// <summary>A committee has passed the time allowed for it, with people still to decide. Goes to the coordinator.</summary>
    CommitteeReviewOverdue,

    /// <summary>A committee is close to the time allowed for it. Goes to the members who have not decided.</summary>
    CommitteeReviewDueSoon
}
