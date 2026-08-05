using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Entities;
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

/// <summary>
/// A seat on an evaluation committee, named rather than numbered so a plan can say who sat on it
/// and a reader can tell one committee from another.
/// </summary>
public enum DemoSeat
{
    ReviewerOne,
    ReviewerTwo,
    ReviewerThree,
    ExternalOne,
    ExternalTwo
}

/// <summary>Which Supervisors a round of proposals went out to.</summary>
public enum DemoDispatch
{
    /// <summary>Both of the department's Supervisors, which is the usual thing to do.</summary>
    Both,

    /// <summary>Only the one whose area it plainly falls in.</summary>
    PrimaryOnly,

    /// <summary>Only the other one, because the first is not taking work on.</summary>
    AlternateOnly
}

/// <summary>Everyone a single demonstration publication needs, resolved to user ids.</summary>
public record DemoCast(
    Guid StudentId,
    Guid CoordinatorId,
    Guid PrimarySupervisorId,
    Guid AlternateSupervisorId,
    Guid HeadOfDepartmentId,
    Guid AdminId,
    IReadOnlyDictionary<DemoSeat, Guid> Seats);

/// <summary>One of the proposals a student wrote, in their own words.</summary>
public record DemoProposal(string Title, string Abstract);

/// <summary>How one committee member voted, and what they said about it.</summary>
public record DemoVote(DemoSeat Seat, bool Approve, string Comments);

/// <summary>
/// What each person said about this publication.
///
/// Every one of these was a single shared constant until the whole dataset read as one person
/// writing the same sentence about twenty different pieces of research. They are per publication
/// now, and unset rather than defaulted: a plan that reaches a step without words for it fails
/// while the seed is being written, which is the only moment anybody can fix it.
/// </summary>
public record DemoWords
{
    /// <summary>The Coordinator, sending the proposals out.</summary>
    public string? Dispatch { get; init; }

    /// <summary>The Supervisors, saying what they would take on.</summary>
    public string? PrimaryOffer { get; init; }
    public string? AlternateOffer { get; init; }

    /// <summary>The Coordinator, refusing the offers and sending the round back.</summary>
    public string? Discard { get; init; }

    /// <summary>The Coordinator, allocating the Supervisor.</summary>
    public string? Allocation { get; init; }

    /// <summary>The Supervisor, on whether ethics documentation is needed.</summary>
    public string? EthicsRequirement { get; init; }

    /// <summary>The Coordinator, agreeing that none is needed.</summary>
    public string? EthicsNotRequired { get; init; }

    /// <summary>The Supervisor, then the Coordinator, then the Head of Department, on the documents.</summary>
    public string? EthicsDocuments { get; init; }
    public string? EthicsCoordinator { get; init; }
    public string? EthicsHead { get; init; }

    /// <summary>The Coordinator, closing the ethics stage.</summary>
    public string? EthicsFinal { get; init; }

    /// <summary>The student, on the draft they are submitting.</summary>
    public string? PaperNotes { get; init; }

    /// <summary>The Supervisor, on the paper itself.</summary>
    public string? PaperSupervisor { get; init; }

    /// <summary>The Administrator, appointing the committee.</summary>
    public string? CommitteeAppointment { get; init; }

    /// <summary>The Coordinator's decision once the committee has finished.</summary>
    public string? PaperDecision { get; init; }

    /// <summary>The author, deciding whether it appears in the catalogue.</summary>
    public string? PublishDecision { get; init; }
}

/// <summary>One demonstration publication: what it is about, how far it has got, and who said what.</summary>
public record DemoPublicationPlan
{
    public required string Title { get; init; }
    public required string Abstract { get; init; }
    public required DemoStage Stage { get; init; }

    /// <summary>
    /// The proposals this student submitted. Three of them, as the institution asks for, and three
    /// separate ideas rather than one title with two suffixes stuck on it.
    /// </summary>
    public DemoProposal[] Proposals { get; init; } = [];

    /// <summary>Which of those the Supervisors backed and the publication went ahead with.</summary>
    public int Chosen { get; init; } = 1;

    /// <summary>Whether the other Supervisor is the one who ends up with it.</summary>
    public bool AlternateSupervises { get; init; }

    public DemoDispatch Dispatch { get; init; } = DemoDispatch.Both;

    public bool EthicsRequired { get; init; } = true;

    public string[]? Keywords { get; init; }
    public int? Year { get; init; }

    /// <summary>
    /// How long ago this publication was opened. Everything it holds is dated back by this, so the
    /// dataset spans a couple of academic years instead of arriving in the same second: dates that
    /// are all equal make every listing ordered by one look broken, because reversing it changes
    /// nothing.
    /// </summary>
    public int StartedDaysAgo { get; init; } = 7;

    /// <summary>Who sat on the evaluation committee.</summary>
    public DemoSeat[] Committee { get; init; } = [];

    /// <summary>How they voted, once they have. Not every committee agrees.</summary>
    public DemoVote[] Votes { get; init; } = [];

    public DemoWords Words { get; init; } = new();
}

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
    ICommitteeService committees,
    ISystemSettingService settings)
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
        await BackdateAsync(containerId, plan.StartedDaysAgo, cancellationToken);

        return containerId;
    }

    private async Task WalkAsync(Guid containerId, DemoCast cast, DemoPublicationPlan plan, CancellationToken ct)
    {
        if (plan.Stage == DemoStage.ProposalsDrafted) return;

        // ---------- Research proposals ----------

        if (plan.Proposals.Length == 0)
        {
            throw new InvalidOperationException(
                $"The demonstration plan '{plan.Title}' reaches {plan.Stage} and carries no proposals.");
        }

        var written = new List<ProposalDto>();
        await StepAsync(async () =>
        {
            foreach (var idea in plan.Proposals)
            {
                written.Add(await proposals.CreateAsync(containerId, cast.StudentId,
                    new SaveProposalRequest(idea.Title, idea.Abstract), ct));
            }

            await proposals.FinishSubmissionAsync(containerId, cast.StudentId, ct);
        });

        if (plan.Stage == DemoStage.ProposalsSubmitted) return;

        // A fortnight to answer, which is what the institution's own setting says and what the
        // screen fills in. Dated rather than left open so the demonstration shows the badge the
        // coordinator reads and the date the supervisor is held to, and so a round that goes
        // nowhere expires the way a real one would.
        var sentTo = plan.Dispatch switch
        {
            DemoDispatch.PrimaryOnly => new[] { cast.PrimarySupervisorId },
            DemoDispatch.AlternateOnly => [cast.AlternateSupervisorId],
            _ => [cast.PrimarySupervisorId, cast.AlternateSupervisorId]
        };

        await StepAsync(() => proposals.SendToSupervisorsAsync(
            new SendToSupervisorsRequest(
                written.Select(p => p.Id).ToList(),
                sentTo,
                Say(plan, plan.Words.Dispatch, nameof(DemoWords.Dispatch)),
                DateTime.UtcNow.AddDays(SettingKeys.DefaultSupervisorResponseDays)),
            cast.CoordinatorId, ct));

        if (plan.Stage == DemoStage.ProposalsWithSupervisors) return;

        var chosen = written[plan.Chosen];

        // A round that found nobody worth allocating. One Supervisor did offer, because the rule
        // is that a round can only be sent back once there is an offer to refuse, and the
        // Coordinator judged that offer not good enough for the student.
        if (plan.Stage == DemoStage.ProposalsReturnedUnwanted)
        {
            await StepAsync(async () =>
            {
                await proposals.SelectAsFeasibleAsync(written[0].Id, cast.PrimarySupervisorId,
                    new SupervisorSelectionRequest(Say(plan, plan.Words.PrimaryOffer, nameof(DemoWords.PrimaryOffer))), ct);

                // Turning that one offer down empties the round, which is the rule: a student comes
                // back only when nothing of theirs has anybody willing.
                await proposals.DiscardSelectionsAsync(written[0].Id,
                    Say(plan, plan.Words.Discard, nameof(DemoWords.Discard)), cast.CoordinatorId, ct);
            });

            return;
        }

        // The Supervisors answer, each backing a different proposal where both were asked, so the
        // Coordinator has a genuine choice to make rather than a single option to rubber-stamp.
        await StepAsync(async () =>
        {
            if (plan.Dispatch != DemoDispatch.AlternateOnly)
            {
                await proposals.SelectAsFeasibleAsync(chosen.Id, cast.PrimarySupervisorId,
                    new SupervisorSelectionRequest(Say(plan, plan.Words.PrimaryOffer, nameof(DemoWords.PrimaryOffer))), ct);
            }

            if (plan.Dispatch != DemoDispatch.PrimaryOnly)
            {
                var alternateTakes = plan.AlternateSupervises ? chosen : written[0];
                await proposals.SelectAsFeasibleAsync(alternateTakes.Id, cast.AlternateSupervisorId,
                    new SupervisorSelectionRequest(Say(plan, plan.Words.AlternateOffer, nameof(DemoWords.AlternateOffer))), ct);
            }
        });

        if (plan.Stage == DemoStage.ProposalSelected) return;

        var supervisorId = plan.AlternateSupervises ? cast.AlternateSupervisorId : cast.PrimarySupervisorId;

        await StepAsync(() => proposals.AssignSupervisorAsync(chosen.Id,
            new AssignSupervisorRequest(supervisorId,
                Say(plan, plan.Words.Allocation, nameof(DemoWords.Allocation))),
            cast.CoordinatorId, ct));

        if (plan.Stage == DemoStage.SupervisorAssigned) return;

        // ---------- Ethics approval ----------

        await StepAsync(() => ethics.SubmitDeclarationAsync(containerId, cast.StudentId,
            new EthicsDeclarationRequest(plan.EthicsRequired ? "Yes" : "No"), ct));

        if (plan.Stage == DemoStage.EthicsDeclared) return;

        if (!plan.EthicsRequired)
        {
            await StepAsync(() => ethics.SubmitSupervisorRequirementDecisionAsync(containerId, supervisorId,
                new SupervisorRequirementDecisionRequest(false,
                    Say(plan, plan.Words.EthicsRequirement, nameof(DemoWords.EthicsRequirement))), ct));

            if (plan.Stage == DemoStage.EthicsNotRequiredAwaitingCoordinator) return;

            await StepAsync(() => ethics.CoordinatorReviewNotRequiredAsync(containerId, cast.CoordinatorId,
                new CoordinatorNotRequiredReviewRequest(false,
                    Say(plan, plan.Words.EthicsNotRequired, nameof(DemoWords.EthicsNotRequired))), ct));

            // Agreeing that no approval is needed is a decision, and by default this institution
            // has its Head of Department see it before the stage closes. So the coordinator has
            // handed on rather than finished, and the stage is still open: walking straight to the
            // research paper from here asked for a stage nothing had opened yet.
            var workflow = await settings.GetEthicsWorkflowSettingsAsync(ct);

            if (workflow.HeadOfDepartmentReviewsWhenNotRequired)
            {
                if (plan.Stage == DemoStage.EthicsWithHeadOfDepartment) return;

                await StepAsync(() => ethics.HeadOfDepartmentReviewAsync(containerId, cast.HeadOfDepartmentId,
                    new HeadOfDepartmentReviewRequest(
                        Say(plan, plan.Words.EthicsHead, nameof(DemoWords.EthicsHead))), ct));

                if (plan.Stage == DemoStage.EthicsAwaitingFinalDecision) return;

                await StepAsync(() => ethics.CoordinatorFinalDecisionAsync(containerId, cast.CoordinatorId,
                    new CoordinatorFinalDecisionRequest(true,
                        Say(plan, plan.Words.EthicsFinal, nameof(DemoWords.EthicsFinal))), ct));
            }
        }
        else
        {
            await StepAsync(() => ethics.SubmitSupervisorRequirementDecisionAsync(containerId, supervisorId,
                new SupervisorRequirementDecisionRequest(true,
                    Say(plan, plan.Words.EthicsRequirement, nameof(DemoWords.EthicsRequirement))), ct));

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

            await StepAsync(() => ethics.SupervisorReviewDocumentsAsync(containerId, supervisorId,
                new DocumentReviewDecisionRequest(true,
                    Say(plan, plan.Words.EthicsDocuments, nameof(DemoWords.EthicsDocuments))), ct));

            if (plan.Stage == DemoStage.EthicsDocumentsWithCoordinator) return;

            await StepAsync(() => ethics.CoordinatorReviewDocumentsAsync(containerId, cast.CoordinatorId,
                new CoordinatorDocumentReviewRequest(true,
                    Say(plan, plan.Words.EthicsCoordinator, nameof(DemoWords.EthicsCoordinator))), ct));

            if (plan.Stage == DemoStage.EthicsWithHeadOfDepartment) return;

            await StepAsync(() => ethics.HeadOfDepartmentReviewAsync(containerId, cast.HeadOfDepartmentId,
                new HeadOfDepartmentReviewRequest(
                    Say(plan, plan.Words.EthicsHead, nameof(DemoWords.EthicsHead))), ct));

            if (plan.Stage == DemoStage.EthicsAwaitingFinalDecision) return;

            await StepAsync(() => ethics.CoordinatorFinalDecisionAsync(containerId, cast.CoordinatorId,
                new CoordinatorFinalDecisionRequest(true,
                    Say(plan, plan.Words.EthicsFinal, nameof(DemoWords.EthicsFinal))), ct));
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
                    plan.Keywords ?? throw new InvalidOperationException(
                        $"The demonstration plan '{plan.Title}' reaches a research paper and names no keywords."),
                    areaIds), ct);

            using var content = new MemoryStream(DemoDocuments.Pdf(plan.Title, plan.Abstract));
            await publications.UploadVersionAsync(paper.Id, cast.StudentId, content, $"{Slug(plan.Title)}.pdf",
                supplementary: null, supplementaryFileName: null,
                reviewerNotes: Say(plan, plan.Words.PaperNotes, nameof(DemoWords.PaperNotes)), ct);

            await publications.SubmitAsync(paper.Id, cast.StudentId, ct);
        });

        if (plan.Stage == DemoStage.PaperWithSupervisor) return;

        await StepAsync(() => publications.SupervisorReviewAsync(paper.Id, supervisorId,
            new PaperReviewDecisionRequest(true,
                Say(plan, plan.Words.PaperSupervisor, nameof(DemoWords.PaperSupervisor))), ct));

        if (plan.Stage == DemoStage.PaperAwaitingCommittee) return;

        if (plan.Committee.Length == 0)
        {
            throw new InvalidOperationException(
                $"The demonstration plan '{plan.Title}' reaches {plan.Stage} and names nobody to sit on its committee.");
        }

        await StepAsync(() => committees.AssignAsync(paper.Id,
            new AssignCommitteeRequest(
                [.. plan.Committee.Select(seat => cast.Seats[seat])], 0,
                Say(plan, plan.Words.CommitteeAppointment, nameof(DemoWords.CommitteeAppointment))),
            cast.AdminId, ct));

        if (plan.Stage == DemoStage.CommitteeReviewing) return;

        var committee = await committees.GetByPublicationAsync(paper.Id, cast.AdminId, ct);
        await StepAsync(async () =>
        {
            foreach (var vote in plan.Votes)
            {
                await committees.MemberReviewAsync(committee.Id, cast.Seats[vote.Seat],
                    new CommitteeMemberReviewRequest(vote.Approve, vote.Comments), ct);
            }
        });

        if (plan.Stage == DemoStage.PaperAwaitingFinalDecision) return;

        await StepAsync(() => publications.CoordinatorFinalDecisionAsync(paper.Id, cast.CoordinatorId,
            new PaperReviewDecisionRequest(true,
                Say(plan, plan.Words.PaperDecision, nameof(DemoWords.PaperDecision))), ct));

        if (plan.Stage == DemoStage.PaperAccepted) return;

        await StepAsync(() => publications.PublishDecisionAsync(paper.Id, cast.StudentId,
            new PublishDecisionRequest(true, Say(plan, plan.Words.PublishDecision, nameof(DemoWords.PublishDecision))),
            cancellationToken: ct));
    }

    /// <summary>
    /// What somebody said at this step, or a failure naming the plan and the sentence it lacks.
    ///
    /// Loud on purpose. A missing sentence used to be impossible because every step had a shared
    /// default, which is exactly how the dataset ended up with one sentence repeated across
    /// eighteen publications; the price of removing the defaults is that a gap has to stop the
    /// seed rather than be quietly filled.
    /// </summary>
    private static string Say(DemoPublicationPlan plan, string? words, string name) =>
        string.IsNullOrWhiteSpace(words)
            ? throw new InvalidOperationException(
                $"The demonstration plan '{plan.Title}' reaches {plan.Stage} without anything for {name}.")
            : words;

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
    /// Moves everything this publication holds back in time by the same amount.
    ///
    /// The services stamp the moment they run, so a dataset built in one pass has every date within
    /// a second of every other. That is not a cosmetic problem: a column of identical dates sorts
    /// the same ascending as descending, so the control that orders by it looks broken, a queue
    /// worked oldest-first has no oldest, and nothing is ever near a deadline. Dating each
    /// publication back by its own amount gives the set the couple of academic years it describes.
    ///
    /// One shift for the whole publication, so the order its own steps happened in survives: the
    /// paper is still submitted after the ethics approval that opened the stage.
    /// </summary>
    private async Task BackdateAsync(Guid containerId, int days, CancellationToken ct)
    {
        if (days <= 0) return;

        // AddDays with a negative number, rather than subtracting a TimeSpan, because that is the
        // form the provider translates; and each nullable date is guarded in the expression itself,
        // since a step this publication never reached has no date to move.
        var back = -(double)days;

        await db.PublicationContainers.Where(c => c.Id == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddDays(back))
                .SetProperty(c => c.UpdatedAt, c => c.UpdatedAt.AddDays(back)), ct);

        await db.ActivityHistoryEntries.Where(e => e.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, e => e.CreatedAt.AddDays(back)), ct);

        await db.ResearchProposals.Where(p => p.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CreatedAt, p => p.CreatedAt.AddDays(back))
                .SetProperty(p => p.UpdatedAt, p => p.UpdatedAt.AddDays(back))
                .SetProperty(p => p.SubmittedAt, p => p.SubmittedAt == null
                    ? null
                    : (DateTime?)p.SubmittedAt.Value.AddDays(back))
                .SetProperty(p => p.ReturnedToDispatchAt, p => p.ReturnedToDispatchAt == null
                    ? null
                    : (DateTime?)p.ReturnedToDispatchAt.Value.AddDays(back)), ct);

        await db.ProposalSupervisorSelections
            .Where(x => x.Proposal.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.InvitedAt, x => x.InvitedAt.AddDays(back))
                .SetProperty(x => x.SelectedAt, x => x.SelectedAt == null
                    ? null
                    : (DateTime?)x.SelectedAt.Value.AddDays(back))
                .SetProperty(x => x.RespondBy, x => x.RespondBy == null
                    ? null
                    : (DateTime?)x.RespondBy.Value.AddDays(back)), ct);

        await db.ProposalAssignments
            .Where(a => a.Proposal.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssignedAt, a => a.AssignedAt.AddDays(back)), ct);

        await db.EthicsDeclarations.Where(d => d.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DecidedAt, d => d.DecidedAt.AddDays(back)), ct);

        await db.EthicsApprovals.Where(a => a.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.CreatedAt, a => a.CreatedAt.AddDays(back))
                .SetProperty(a => a.StepEnteredAt, a => a.StepEnteredAt == null
                    ? null
                    : (DateTime?)a.StepEnteredAt.Value.AddDays(back))
                .SetProperty(a => a.SupervisorDecisionAt, a => a.SupervisorDecisionAt == null
                    ? null
                    : (DateTime?)a.SupervisorDecisionAt.Value.AddDays(back))
                .SetProperty(a => a.SupervisorDocumentsReviewedAt, a => a.SupervisorDocumentsReviewedAt == null
                    ? null
                    : (DateTime?)a.SupervisorDocumentsReviewedAt.Value.AddDays(back))
                .SetProperty(a => a.CoordinatorDecisionAt, a => a.CoordinatorDecisionAt == null
                    ? null
                    : (DateTime?)a.CoordinatorDecisionAt.Value.AddDays(back))
                .SetProperty(a => a.HeadOfDepartmentReviewedAt, a => a.HeadOfDepartmentReviewedAt == null
                    ? null
                    : (DateTime?)a.HeadOfDepartmentReviewedAt.Value.AddDays(back))
                .SetProperty(a => a.FinalDecisionAt, a => a.FinalDecisionAt == null
                    ? null
                    : (DateTime?)a.FinalDecisionAt.Value.AddDays(back))
                .SetProperty(a => a.ApprovalDate, a => a.ApprovalDate == null
                    ? null
                    : (DateTime?)a.ApprovalDate.Value.AddDays(back)), ct);

        await db.EthicsDocuments.Where(d => d.EthicsApproval.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UploadedAt, d => d.UploadedAt.AddDays(back)), ct);

        await db.Publications.Where(p => p.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CreatedAt, p => p.CreatedAt.AddDays(back))
                .SetProperty(p => p.UpdatedAt, p => p.UpdatedAt.AddDays(back))
                .SetProperty(p => p.PublishedAt, p => p.PublishedAt == null
                    ? null
                    : (DateTime?)p.PublishedAt.Value.AddDays(back)), ct);

        await db.PublicationVersions.Where(v => v.Publication.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.UploadedAt, v => v.UploadedAt.AddDays(back)), ct);

        await db.Reviews.Where(r => r.PublicationVersion.Publication.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedAt, r => r.ReviewedAt.AddDays(back)), ct);

        await db.Committees.Where(c => c.Publication.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddDays(back)), ct);

        await db.CommitteeMembers.Where(m => m.Committee.Publication.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.InvitedAt, m => m.InvitedAt.AddDays(back))
                .SetProperty(m => m.DecidedAt, m => m.DecidedAt == null
                    ? null
                    : (DateTime?)m.DecidedAt.Value.AddDays(back)), ct);

        // Both of these carry the container as a loose reference rather than a foreign key, which
        // is why they are matched by id rather than joined.
        await db.Notifications.Where(n => n.RelatedEntityId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, n => n.CreatedAt.AddDays(back)), ct);

        await db.AuditLogEntries.Where(e => e.EntityId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Timestamp, e => e.Timestamp.AddDays(back)), ct);
    }

    private static string Slug(string text)
    {
        var cleaned = new string(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        return string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }
}
