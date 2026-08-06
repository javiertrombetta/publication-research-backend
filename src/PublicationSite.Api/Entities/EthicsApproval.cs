using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class EthicsApproval : IHaveAConcurrencyStamp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public EthicsStatus Status { get; set; } = EthicsStatus.PendingSupervisorDecision;

    public string? ReferenceNumber { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public bool? IsRequiredPerSupervisor { get; set; }
    public string? SupervisorDecisionComments { get; set; }
    public DateTime? SupervisorDecisionAt { get; set; }

    /// <summary>
    /// When the supervisor finished reading the uploaded documents.
    ///
    /// A mark of their own, rather than reading it off the documents' status. While the supervisor
    /// always went first, "nothing is still PendingReview" meant "the supervisor has read it", and
    /// that only holds while they are the first reader: an institution that puts the coordinator
    /// first needs the two readings told apart by something belonging to each of them.
    /// </summary>
    public DateTime? SupervisorDocumentsReviewedAt { get; set; }

    public bool? IsRequiredPerCoordinator { get; set; }
    public string? CoordinatorDecisionComments { get; set; }
    public DateTime? CoordinatorDecisionAt { get; set; }

    /// <summary>
    /// Which head of department this decision was put to.
    ///
    /// A department can have more than one, and without naming one the review belonged to all of
    /// them and therefore to nobody: everyone saw it on their queue and each could reasonably
    /// assume somebody else had it. It is chosen when the coordinator hands on, from the heads of
    /// the student's own department, and an administrator can change it afterwards.
    /// </summary>
    public Guid? HeadOfDepartmentUserId { get; set; }
    public ApplicationUser? HeadOfDepartmentUser { get; set; }

    public string? HeadOfDepartmentComments { get; set; }
    public DateTime? HeadOfDepartmentReviewedAt { get; set; }

    /// <summary>
    /// When this approval last became somebody's turn.
    ///
    /// A deadline is measured from the moment work landed on a person, and nothing recorded that:
    /// the timestamps here say when each decision was made, which is the opposite end. Without it
    /// an overdue review could only be guessed at from whichever mark happened to be newest.
    /// </summary>
    public DateTime? StepEnteredAt { get; set; }

    /// <summary>
    /// When the coordinator was told this review had run out of time, and when whoever owes it was
    /// warned it was about to. Both so the sweep says each thing once rather than every time it
    /// runs, and both cleared when the work moves on.
    /// </summary>
    public DateTime? OverdueReportedAt { get; set; }
    public DateTime? DueSoonWarnedAt { get; set; }

    public DateTime? FinalDecisionAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EthicsDocument> Documents { get; set; } = [];

    /// <summary>
    /// The documents this approval asks for, as they stood when documentation was requested.
    /// See EthicsApprovalRequirement for why the list is copied rather than read live.
    /// </summary>
    public ICollection<EthicsApprovalRequirement> RequiredDocuments { get; set; } = [];

    /// <summary>
    /// Changed on every save, and part of the WHERE clause of every UPDATE. See
    /// <see cref="IHaveAConcurrencyStamp"/> for why a decision needs one.
    /// </summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
