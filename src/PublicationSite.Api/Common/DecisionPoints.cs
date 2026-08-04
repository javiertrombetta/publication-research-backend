namespace PublicationSite.Api.Common;

/// <summary>
/// Every decision in the pipeline that carries a comment, named once.
///
/// Whether a comment is required used to be written into each validator, which meant the answer
/// lived in fifteen places and an institution that wanted it the other way round had to be given a
/// new build. It is a policy, not a rule of the software: some places want a reason for everything
/// on the record, others want one only where somebody has to act on it.
///
/// The keys are stored settings, so they outlive renames of the code around them and must not be
/// changed once a deployment has written to them. The display names are what an administrator
/// reads on the settings screen and what a refusal quotes back.
/// </summary>
public static class DecisionPoints
{
    /// <summary>The prefix every stored key carries. See SettingKeys.</summary>
    public const string Prefix = "comments.";

    /// <param name="Key">Stored as <c>comments.{Key}</c>. Never change one that has been deployed.</param>
    /// <param name="Stage">Which part of the pipeline it belongs to, for grouping on the screen.</param>
    /// <param name="Name">What the decision is, in the words the screen uses for it.</param>
    /// <param name="RequiredByDefault">
    /// What this deployment starts with. Sending work back defaults to required, because the
    /// comment is the whole of what the person receiving it has to work from. Passing work on
    /// defaults to optional: there is nothing to explain and nobody waiting to be told.
    /// </param>
    public record DecisionPoint(string Key, string Stage, string Name, bool RequiredByDefault);

    public const string ProposalStage = "Research proposals";
    public const string EthicsStage = "Ethics approval";
    public const string PaperStage = "Research paper";

    // ---------- Research proposals ----------

    public const string ProposalSendToSupervisors = "proposal.send-to-supervisors";
    public const string ProposalDeferToNextCycle = "proposal.defer-to-next-cycle";
    public const string ProposalSupervisorSelection = "proposal.supervisor-selection";
    public const string ProposalCoordinatorAssign = "proposal.coordinator-assign";
    public const string ProposalCoordinatorDiscard = "proposal.coordinator-discard";
    public const string ProposalRequestNewRound = "proposal.request-new-round";

    // ---------- Ethics ----------

    public const string EthicsSupervisorRuling = "ethics.supervisor-ruling";
    public const string EthicsSupervisorDocumentsAccept = "ethics.supervisor-documents-accept";
    public const string EthicsSupervisorDocumentsReturn = "ethics.supervisor-documents-return";
    public const string EthicsCoordinatorConfirmNotRequired = "ethics.coordinator-confirm-not-required";
    public const string EthicsCoordinatorOverturnNotRequired = "ethics.coordinator-overturn-not-required";
    public const string EthicsCoordinatorDocumentsApprove = "ethics.coordinator-documents-approve";
    public const string EthicsCoordinatorDocumentsReturn = "ethics.coordinator-documents-return";
    public const string EthicsHeadOfDepartmentReview = "ethics.head-of-department-review";
    public const string EthicsCoordinatorFinalApprove = "ethics.coordinator-final-approve";
    public const string EthicsCoordinatorFinalReturn = "ethics.coordinator-final-return";

    // ---------- Research paper ----------

    public const string PaperSupervisorAccept = "paper.supervisor-accept";
    public const string PaperSupervisorReturn = "paper.supervisor-return";
    public const string PaperCommitteeApprove = "paper.committee-approve";
    public const string PaperCommitteeReject = "paper.committee-reject";
    public const string PaperCoordinatorAccept = "paper.coordinator-accept";
    public const string PaperCoordinatorReturn = "paper.coordinator-return";
    public const string PaperCommitteeAssign = "paper.committee-assign";
    public const string PaperCommitteeAssignOverride = "paper.committee-assign-override";
    public const string PaperPublishOnBehalf = "paper.publish-on-behalf";
    public const string PaperWithdrawFromCatalogue = "paper.withdraw-from-catalogue";

    /// <summary>
    /// The whole set, in the order the pipeline runs, which is the order the settings screen shows
    /// them in. A decision missing from here has no setting and no place on that screen, so adding
    /// one to the pipeline means adding it here.
    /// </summary>
    public static readonly IReadOnlyList<DecisionPoint> All =
    [
        new(ProposalSendToSupervisors, ProposalStage, "Coordinator: send proposals out to supervisors", false),
        new(ProposalSupervisorSelection, ProposalStage, "Supervisor: willing to supervise a proposal", false),
        new(ProposalCoordinatorAssign, ProposalStage, "Coordinator: appoint the supervisor", false),
        new(ProposalCoordinatorDiscard, ProposalStage, "Coordinator: turn down every offer on a proposal", true),
        new(ProposalRequestNewRound, ProposalStage, "Coordinator: ask the student for a new round of proposals", true),
        new(ProposalDeferToNextCycle, ProposalStage, "Coordinator: hold a round over to the next cycle", true),

        new(EthicsSupervisorRuling, EthicsStage, "Supervisor: rule whether ethics documentation is required", true),
        new(EthicsSupervisorDocumentsAccept, EthicsStage, "Supervisor: accept the ethics documents", false),
        new(EthicsSupervisorDocumentsReturn, EthicsStage, "Supervisor: send ethics documents back", true),
        new(EthicsCoordinatorConfirmNotRequired, EthicsStage, "Coordinator: confirm no documentation is needed", false),
        new(EthicsCoordinatorOverturnNotRequired, EthicsStage, "Coordinator: require documentation after all", true),
        new(EthicsCoordinatorDocumentsApprove, EthicsStage, "Coordinator: approve the ethics documents", false),
        new(EthicsCoordinatorDocumentsReturn, EthicsStage, "Coordinator: send ethics documents back", true),
        new(EthicsHeadOfDepartmentReview, EthicsStage, "Head of Department: comment on the ethics documents", true),
        new(EthicsCoordinatorFinalApprove, EthicsStage, "Coordinator: close ethics as approved", false),
        new(EthicsCoordinatorFinalReturn, EthicsStage, "Coordinator: send ethics back at the final decision", true),

        new(PaperSupervisorAccept, PaperStage, "Supervisor: accept the paper and send it to the committee", false),
        new(PaperSupervisorReturn, PaperStage, "Supervisor: send the paper back for revision", true),
        new(PaperCommitteeAssign, PaperStage, "Administrator: appoint the evaluation committee", false),
        new(PaperCommitteeAssignOverride, PaperStage, "Administrator: appoint a committee of a different shape", true),
        new(PaperCommitteeApprove, PaperStage, "Committee member: approve the paper", false),
        new(PaperCommitteeReject, PaperStage, "Committee member: do not approve the paper", true),
        new(PaperCoordinatorAccept, PaperStage, "Coordinator: accept the paper", false),
        new(PaperCoordinatorReturn, PaperStage, "Coordinator: send the paper back for revision", true),
        new(PaperPublishOnBehalf, PaperStage, "Publishing decision made on a student's behalf", true),
        new(PaperWithdrawFromCatalogue, PaperStage, "Administrator: withdraw a paper from the catalogue", true)
    ];

    public static DecisionPoint? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string SettingKeyFor(string key) => Prefix + key;
}
