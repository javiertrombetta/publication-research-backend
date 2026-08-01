using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Data;

/// <summary>
/// The point in the process a demonstration publication is parked at. Each value is somebody's
/// turn to act, so between them they put a piece of work in front of every role at every decision
/// the system asks anyone to make.
/// </summary>
public enum DemoStage
{
    /// <summary>Open, with nothing written yet. The student's turn.</summary>
    ProposalsDrafted,

    /// <summary>Proposals submitted and waiting to be sent out. The Coordinator's turn.</summary>
    ProposalsSubmitted,

    /// <summary>With the Supervisors, none of whom has answered yet.</summary>
    ProposalsWithSupervisors,

    /// <summary>Supervisors have said what they would take on. The Coordinator allocates.</summary>
    ProposalSelected,

    /// <summary>
    /// A round that found nobody. Everything went back to the dispatch queue, where the Coordinator
    /// can send it to different Supervisors or ask the student to write new proposals.
    /// </summary>
    ProposalsReturnedUnwanted,

    /// <summary>Supervisor allocated; the ethics declaration is the student's next move.</summary>
    SupervisorAssigned,

    /// <summary>Declared. The Supervisor decides whether documentation is needed.</summary>
    EthicsDeclared,

    /// <summary>A Supervisor said none is needed; the Coordinator confirms or overrules.</summary>
    EthicsNotRequiredAwaitingCoordinator,

    /// <summary>Documentation asked for. The student uploads it.</summary>
    EthicsDocumentsRequested,

    /// <summary>Uploaded and unread. The Supervisor looks first.</summary>
    EthicsDocumentsUploaded,

    /// <summary>The Supervisor accepted them. The Coordinator reviews.</summary>
    EthicsDocumentsWithCoordinator,

    /// <summary>The Coordinator approved them. The Head of Department comments.</summary>
    EthicsWithHeadOfDepartment,

    /// <summary>Everyone has had their say. The Coordinator closes the ethics stage.</summary>
    EthicsAwaitingFinalDecision,

    /// <summary>Ethics settled. The student writes and submits the paper.</summary>
    EthicsCompleted,

    /// <summary>Submitted. The Supervisor reviews the paper itself.</summary>
    PaperWithSupervisor,

    /// <summary>Approved by the Supervisor. An Admin appoints the evaluation committee.</summary>
    PaperAwaitingCommittee,

    /// <summary>Appointed, and nobody has voted. The committee members' turn.</summary>
    CommitteeReviewing,

    /// <summary>The committee has finished. The Coordinator decides.</summary>
    PaperAwaitingFinalDecision,

    /// <summary>Accepted. Only the author decides whether it is published.</summary>
    PaperAccepted,

    /// <summary>Published, and visible in the public catalogue.</summary>
    Published
}

/// <summary>Everyone a single demonstration publication needs, resolved to user ids.</summary>
public record DemoCast(
    Guid StudentId,
    Guid CoordinatorId,
    Guid PrimarySupervisorId,
    Guid AlternateSupervisorId,
    Guid HeadOfDepartmentId,
    Guid AdminId,
    IReadOnlyList<Guid> CommitteeMemberIds);

/// <summary>One demonstration publication: what it is about, and how far it has got.</summary>
public record DemoPublicationPlan(
    string Title,
    string Abstract,
    DemoStage Stage,
    bool EthicsRequired = true,
    string[]? Keywords = null,
    int? Year = null);

/// <summary>
/// Walks a publication from nothing to wherever its plan says it stops, by calling the same service
/// methods the interface calls.
///
/// Writing the rows directly would have been shorter and would have been wrong: the state a
/// publication is in is spread across the container, its proposals, the ethics approval, the paper,
/// its versions, the committee, the activity history and the notifications, and every one of those
/// has to agree. Going through the services means the demonstration data can only ever be in a
/// state the application itself can produce, and if a rule changes, the seed changes with it
/// instead of quietly becoming a set of records nothing can explain.
/// </summary>
public class DemoPipelineBuilder(
    ApplicationDbContext db,
    IContainerService containers,
    IProposalService proposals,
    IEthicsService ethics,
    IPublicationService publications,
    ICommitteeService committees)
{
    /// <summary>
    /// When the step currently running began. Everything a publication generated before its last
    /// step is history and is marked read; what the last step raised is the alert the person
    /// whose turn it is should find waiting. Without this every inbox would hold one notification
    /// per transition and none of them would mean anything.
    /// </summary>
    private DateTime _currentStepStartedAt;

    public async Task<Guid> BuildAsync(DemoCast cast, DemoPublicationPlan plan, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;

        // Reset per publication rather than left holding the previous one's last step: a plan that
        // stops before its first step would otherwise mark its notifications against a boundary
        // belonging to a different publication entirely.
        _currentStepStartedAt = startedAt;

        var container = await containers.CreateAsync(cast.StudentId, cancellationToken);
        var containerId = container.Id;

        await WalkAsync(containerId, cast, plan, cancellationToken);
        await MarkEarlierNotificationsReadAsync(startedAt, cancellationToken);

        return containerId;
    }

    private async Task WalkAsync(Guid containerId, DemoCast cast, DemoPublicationPlan plan, CancellationToken ct)
    {
        if (plan.Stage == DemoStage.ProposalsDrafted) return;

        // ---------- Research proposals ----------

        var written = new List<ProposalDto>();
        await StepAsync(async () =>
        {
            foreach (var (title, summary) in ProposalIdeasFor(plan))
            {
                written.Add(await proposals.CreateAsync(containerId, cast.StudentId, new SaveProposalRequest(title, summary), ct));
            }

            await proposals.FinishSubmissionAsync(containerId, cast.StudentId, ct);
        });

        if (plan.Stage == DemoStage.ProposalsSubmitted) return;

        // A fortnight to answer, which is what the institution's own setting says and what the
        // screen fills in. Dated rather than left open so the demonstration shows the badge the
        // coordinator reads and the date the supervisor is held to, and so a round that goes
        // nowhere expires the way a real one would.
        await StepAsync(() => proposals.SendToSupervisorsAsync(
            new SendToSupervisorsRequest(
                written.Select(p => p.Id).ToList(),
                [cast.PrimarySupervisorId, cast.AlternateSupervisorId],
                "Sent to both Supervisors in the department for consideration.",
                DateTime.UtcNow.AddDays(SettingKeys.DefaultSupervisorResponseDays)),
            cast.CoordinatorId, ct));

        if (plan.Stage == DemoStage.ProposalsWithSupervisors) return;

        // Nobody was interested, so the whole set went back to the dispatch queue. The coordinator
        // either sends it to different supervisors or asks the student for new proposals, and both
        // of those are on the Send proposals screen waiting to be tried.
        if (plan.Stage == DemoStage.ProposalsReturnedUnwanted)
        {
            await StepAsync(async () =>
            {
                await proposals.SelectAsFeasibleAsync(written[0].Id, cast.PrimarySupervisorId,
                    new SupervisorSelectionRequest("Interesting, but not close enough to what I supervise."), ct);

                // Turning that one offer down empties the round, which is the rule: a student comes
                // back only when nothing of theirs has anybody willing.
                await proposals.DiscardSelectionsAsync(written[0].Id,
                    "Neither reply engaged with the question the student is actually asking.",
                    cast.CoordinatorId, ct);
            });

            return;
        }

        // Two Supervisors answer, each backing a different proposal, so the Coordinator has a
        // genuine choice to make rather than a single option to rubber-stamp.
        var chosen = written[1];
        await StepAsync(async () =>
        {
            await proposals.SelectAsFeasibleAsync(chosen.Id, cast.PrimarySupervisorId,
                new SupervisorSelectionRequest("This sits squarely within my area and I have capacity this cycle."), ct);

            await proposals.SelectAsFeasibleAsync(written[0].Id, cast.AlternateSupervisorId,
                new SupervisorSelectionRequest("Feasible, though the scope would need narrowing before it starts."), ct);
        });

        if (plan.Stage == DemoStage.ProposalSelected) return;

        await StepAsync(() => proposals.AssignSupervisorAsync(chosen.Id,
            new AssignSupervisorRequest(cast.PrimarySupervisorId,
                "Allocated on the strength of the Supervisor's expertise and current workload."),
            cast.CoordinatorId, ct));

        if (plan.Stage == DemoStage.SupervisorAssigned) return;

        // ---------- Ethics approval ----------

        await StepAsync(() => ethics.SubmitDeclarationAsync(containerId, cast.StudentId,
            new EthicsDeclarationRequest(plan.EthicsRequired ? "Yes" : "No"), ct));

        if (plan.Stage == DemoStage.EthicsDeclared) return;

        if (!plan.EthicsRequired)
        {
            await StepAsync(() => ethics.SubmitSupervisorRequirementDecisionAsync(containerId, cast.PrimarySupervisorId,
                new SupervisorRequirementDecisionRequest(false,
                    "The study works entirely from published, anonymised data, so no approval is needed."), ct));

            if (plan.Stage == DemoStage.EthicsNotRequiredAwaitingCoordinator) return;

            await StepAsync(() => ethics.CoordinatorReviewNotRequiredAsync(containerId, cast.CoordinatorId,
                new CoordinatorNotRequiredReviewRequest(false,
                    "Reviewed and agreed: no human participants and no identifiable data."), ct));
        }
        else
        {
            await StepAsync(() => ethics.SubmitSupervisorRequirementDecisionAsync(containerId, cast.PrimarySupervisorId,
                new SupervisorRequirementDecisionRequest(true,
                    "The study interviews participants, so full ethics documentation is required."), ct));

            if (plan.Stage == DemoStage.EthicsDocumentsRequested) return;

            await StepAsync(async () =>
            {
                var required = await ethics.GetRequiredDocumentsAsync(containerId, cast.StudentId, ct);
                foreach (var document in required)
                {
                    using var content = new MemoryStream(DemoDocuments.Pdf(document.Name, plan.Title));
                    await ethics.UploadDocumentAsync(containerId, cast.StudentId, document.Name, content,
                        $"{Slug(document.Name)}.pdf", ct);
                }
            });

            if (plan.Stage == DemoStage.EthicsDocumentsUploaded) return;

            await StepAsync(() => ethics.SupervisorReviewDocumentsAsync(containerId, cast.PrimarySupervisorId,
                new DocumentReviewDecisionRequest(true,
                    "Complete and consistent with the study as described. Passed to the Coordinator."), ct));

            if (plan.Stage == DemoStage.EthicsDocumentsWithCoordinator) return;

            await StepAsync(() => ethics.CoordinatorReviewDocumentsAsync(containerId, cast.CoordinatorId,
                new CoordinatorDocumentReviewRequest(true,
                    "Checked against the institutional policy. Referred to the Head of Department."), ct));

            if (plan.Stage == DemoStage.EthicsWithHeadOfDepartment) return;

            await StepAsync(() => ethics.HeadOfDepartmentReviewAsync(containerId, cast.HeadOfDepartmentId,
                new HeadOfDepartmentReviewRequest(
                    "No concerns from the department. The consent wording is clear and the data plan is proportionate."), ct));

            if (plan.Stage == DemoStage.EthicsAwaitingFinalDecision) return;

            await StepAsync(() => ethics.CoordinatorFinalDecisionAsync(containerId, cast.CoordinatorId,
                new CoordinatorFinalDecisionRequest(true, "Ethics approval granted. The research paper stage is now open."), ct));
        }

        if (plan.Stage == DemoStage.EthicsCompleted) return;

        // ---------- Research paper ----------

        var paper = await publications.GetOrCreateDraftAsync(containerId, cast.StudentId, ct);

        await StepAsync(async () =>
        {
            var areaIds = await db.ResearchAreas.Select(a => a.Id).Take(2).ToListAsync(ct);

            await publications.UpdateMetadataAsync(paper.Id, cast.StudentId,
                new UpdatePublicationMetadataRequest(
                    plan.Title,
                    plan.Abstract,
                    "Research paper",
                    plan.Year ?? DateTime.UtcNow.Year,
                    plan.Keywords ?? ["research methods", "higher education"],
                    areaIds), ct);

            using var content = new MemoryStream(DemoDocuments.Pdf(plan.Title, plan.Abstract));
            await publications.UploadVersionAsync(paper.Id, cast.StudentId, content, $"{Slug(plan.Title)}.pdf",
                supplementary: null, supplementaryFileName: null,
                reviewerNotes: "First complete draft, following the structure agreed with my Supervisor.", ct);

            await publications.SubmitAsync(paper.Id, cast.StudentId, ct);
        });

        if (plan.Stage == DemoStage.PaperWithSupervisor) return;

        await StepAsync(() => publications.SupervisorReviewAsync(paper.Id, cast.PrimarySupervisorId,
            new PaperReviewDecisionRequest(true,
                "The argument holds and the methodology is sound. Ready for the evaluation committee."), ct));

        if (plan.Stage == DemoStage.PaperAwaitingCommittee) return;

        await StepAsync(() => committees.AssignAsync(paper.Id,
            new AssignCommitteeRequest(cast.CommitteeMemberIds, 0,
                "Committee appointed to the composition this publication was opened under."),
            cast.AdminId, ct));

        if (plan.Stage == DemoStage.CommitteeReviewing) return;

        var committee = await committees.GetByPublicationAsync(paper.Id, cast.AdminId, ct);
        await StepAsync(async () =>
        {
            foreach (var member in committee.Members)
            {
                await committees.MemberReviewAsync(committee.Id, member.UserId,
                    new CommitteeMemberReviewRequest(true,
                        "A solid contribution. My comments are minor and editorial rather than substantive."), ct);
            }
        });

        if (plan.Stage == DemoStage.PaperAwaitingFinalDecision) return;

        await StepAsync(() => publications.CoordinatorFinalDecisionAsync(paper.Id, cast.CoordinatorId,
            new PaperReviewDecisionRequest(true, "Accepted. The author may now decide whether to publish it."), ct));

        if (plan.Stage == DemoStage.PaperAccepted) return;

        await StepAsync(() => publications.PublishDecisionAsync(paper.Id, cast.StudentId,
            new PublishDecisionRequest(true, "I am happy for this to appear in the public catalogue."),
            cancellationToken: ct));
    }

    private async Task StepAsync(Func<Task> step)
    {
        _currentStepStartedAt = DateTime.UtcNow;
        await step();
    }

    /// <summary>
    /// Everything this publication raised before its final step becomes read history. CreatedAt is
    /// stamped by the application rather than the database, so the boundary is exact and needs no
    /// help from the two clocks agreeing.
    /// </summary>
    private async Task MarkEarlierNotificationsReadAsync(DateTime startedAt, CancellationToken ct)
    {
        await db.Notifications
            .Where(n => n.CreatedAt >= startedAt && n.CreatedAt < _currentStepStartedAt && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), ct);
    }

    /// <summary>
    /// Three proposals, as a student submits them: the one the plan is named after, and two
    /// alternatives that read as real alternatives rather than filler.
    /// </summary>
    private static (string Title, string Abstract)[] ProposalIdeasFor(DemoPublicationPlan plan) =>
    [
        ($"{plan.Title}: a preliminary scoping study",
            $"An initial scoping of the same question, narrower in scope than the main proposal. {plan.Abstract}"),
        (plan.Title, plan.Abstract),
        ($"{plan.Title}: a comparative approach",
            $"The same question approached comparatively across two cohorts rather than one. {plan.Abstract}")
    ];

    private static string Slug(string text)
    {
        var cleaned = new string(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        return string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }
}
