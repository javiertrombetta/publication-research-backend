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

    /// <summary>
    /// The institution's research areas this paper belongs to, by name.
    ///
    /// Named per publication rather than taken from the top of the list, which is what the builder
    /// used to do: every paper came out tagged with the same first two, so a study of procurement
    /// in the Business department was filed under Computing Education, and four of the six areas
    /// were attached to nothing at all. The catalogue filters by these, and a filter where every
    /// row carries the same value is not a filter.
    /// </summary>
    public string[]? Areas { get; init; }

    /// <summary>
    /// What kind of publication it is, in the words the student's own form offers: a journal
    /// article, a conference proceeding, a thesis or a technical report.
    ///
    /// The builder used to write "Research paper" for every one of them, which is not one of the
    /// four. Opening a seeded paper in the editor showed a dropdown with nothing selected, and the
    /// catalogue's filter by type had a single value in it, so it could not be told from a filter
    /// that does nothing.
    /// </summary>
    public string? Type { get; init; }

    public int? Year { get; init; }

    /// <summary>
    /// How long ago this publication was opened. Everything it holds is dated back by this, so the
    /// dataset spans a couple of academic years instead of arriving in the same second: dates that
    /// are all equal make every listing ordered by one look broken, because reversing it changes
    /// nothing.
    /// </summary>
    public int StartedDaysAgo { get; init; } = 7;

    /// <summary>
    /// How long ago the last thing happened to it. The steps in between are spread evenly across
    /// the gap, so a publication has a history with duration in it rather than a column of one
    /// date repeated.
    ///
    /// Left at zero this means today, which is right for work that is still moving: whoever it is
    /// waiting on was handed it recently. A publication that finished names the day it finished.
    /// </summary>
    public int LastActionDaysAgo { get; init; }

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

    /// <summary>
    /// Where each step of the publication being built began, on the real clock. The steps run
    /// milliseconds apart, so this is what lets each of them be moved to a date of its own
    /// afterwards rather than the whole publication being moved as one block.
    /// </summary>
    private readonly List<DateTime> _stepStarts = [];

    public async Task<Guid> BuildAsync(DemoCast cast, DemoPublicationPlan plan, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        _stepStarts.Clear();
        _stepStarts.Add(startedAt);

        // Reset per publication rather than left holding the previous one's last step: a plan that
        // stops before its first step would otherwise mark its notifications against a boundary
        // belonging to a different publication entirely.
        _currentStepStartedAt = startedAt;

        var container = await containers.CreateAsync(cast.StudentId, cancellationToken);
        var containerId = container.Id;

        await WalkAsync(containerId, cast, plan, cancellationToken);
        await MarkEarlierNotificationsReadAsync(startedAt, cancellationToken);
        await BackdateAsync(containerId, plan, startedAt, cancellationToken);

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
            var wanted = plan.Areas ?? throw new InvalidOperationException(
                $"The demonstration plan '{plan.Title}' reaches a research paper and names no research areas.");

            var areaIds = await db.ResearchAreas
                .Where(a => wanted.Contains(a.Name))
                .Select(a => a.Id)
                .ToListAsync(ct);

            if (areaIds.Count != wanted.Length)
            {
                throw new InvalidOperationException(
                    $"The demonstration plan '{plan.Title}' names a research area the institution does not have: "
                    + string.Join(", ", wanted));
            }

            await publications.UpdateMetadataAsync(paper.Id, cast.StudentId,
                new UpdatePublicationMetadataRequest(
                    plan.Title,
                    plan.Abstract,
                    plan.Type ?? throw new InvalidOperationException(
                        $"The demonstration plan '{plan.Title}' reaches a research paper and says nothing about what kind it is."),
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
        _stepStarts.Add(_currentStepStartedAt);
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
    /// <summary>
    /// Moves what this publication holds back to when it would actually have happened.
    ///
    /// The services stamp the moment they run, so a dataset built in one pass has every date within
    /// a second of every other. Dating each publication back by its own amount fixed the listings,
    /// which order by when a publication started, and left a second version of the same fault
    /// inside each one: a paper created, reviewed by two supervisors, put through ethics, sent to a
    /// committee and published, all on one afternoon. Nobody reading a publication's history
    /// believes that, and "how long has this been sitting with the Head of Department" has no
    /// answer when every entry shares a timestamp.
    ///
    /// So each step is moved separately. The steps ran milliseconds apart and their real boundaries
    /// were recorded as they went, which is enough to tell the rows of one step from another's;
    /// each is then placed on its own date, spread evenly between the day the publication opened
    /// and the day of its last action. Order is preserved by construction, since the dates are laid
    /// out in the same sequence the steps ran in.
    /// </summary>
    private async Task BackdateAsync(
        Guid containerId, DemoPublicationPlan plan, DateTime startedAt, CancellationToken ct)
    {
        if (plan.StartedDaysAgo <= 0) return;

        var now = DateTime.UtcNow;

        // Where the last step should land. A publication still in flight was last touched recently,
        // which is what makes a queue look like a queue; a finished one ended when it ended.
        var lastActionDaysAgo = Math.Min(plan.LastActionDaysAgo, plan.StartedDaysAgo);

        // The real windows, and the date each is being moved to.
        var windows = new List<(DateTime From, DateTime To, double Days)>();
        for (var i = 0; i < _stepStarts.Count; i++)
        {
            var from = _stepStarts[i];
            var to = i + 1 < _stepStarts.Count ? _stepStarts[i + 1] : now.AddSeconds(1);

            // Evenly between the two ends, oldest first. One step means the day it opened.
            var share = _stepStarts.Count == 1 ? 0d : (double)i / (_stepStarts.Count - 1);
            var days = plan.StartedDaysAgo - share * (plan.StartedDaysAgo - lastActionDaysAgo);

            windows.Add((from, to, days));
        }

        foreach (var (from, to, days) in windows)
        {
            await ShiftAsync(containerId, from, to, -days, ct);
        }
    }

    /// <summary>
    /// Moves every date this publication owns that falls inside one real window.
    ///
    /// The window is on the real clock, so a row already moved is days in the past and cannot be
    /// caught twice however many windows follow. Each nullable date is guarded in the expression
    /// itself, since a step this publication never reached has no date to move, and AddDays with a
    /// negative number is used rather than subtracting a TimeSpan because that is the form the
    /// provider translates.
    /// </summary>
    private async Task ShiftAsync(Guid containerId, DateTime from, DateTime to, double back, CancellationToken ct)
    {
        await db.PublicationContainers.Where(c => c.Id == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.CreatedAt, c => c.CreatedAt >= from && c.CreatedAt < to ? c.CreatedAt.AddDays(back) : c.CreatedAt)
                .SetProperty(c => c.UpdatedAt, c => c.UpdatedAt >= from && c.UpdatedAt < to ? c.UpdatedAt.AddDays(back) : c.UpdatedAt), ct);

        await db.ActivityHistoryEntries
            .Where(e => e.PublicationContainerId == containerId && e.CreatedAt >= from && e.CreatedAt < to)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, e => e.CreatedAt.AddDays(back)), ct);

        await db.ResearchProposals.Where(p => p.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CreatedAt, p => p.CreatedAt >= from && p.CreatedAt < to ? p.CreatedAt.AddDays(back) : p.CreatedAt)
                .SetProperty(p => p.UpdatedAt, p => p.UpdatedAt >= from && p.UpdatedAt < to ? p.UpdatedAt.AddDays(back) : p.UpdatedAt)
                .SetProperty(p => p.SubmittedAt, p => p.SubmittedAt != null && p.SubmittedAt >= from && p.SubmittedAt < to
                    ? (DateTime?)p.SubmittedAt.Value.AddDays(back) : p.SubmittedAt)
                .SetProperty(p => p.ReturnedToDispatchAt, p => p.ReturnedToDispatchAt != null && p.ReturnedToDispatchAt >= from && p.ReturnedToDispatchAt < to
                    ? (DateTime?)p.ReturnedToDispatchAt.Value.AddDays(back) : p.ReturnedToDispatchAt), ct);

        await db.ProposalSupervisorSelections.Where(x => x.Proposal.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.InvitedAt, x => x.InvitedAt >= from && x.InvitedAt < to ? x.InvitedAt.AddDays(back) : x.InvitedAt)
                .SetProperty(x => x.SelectedAt, x => x.SelectedAt != null && x.SelectedAt >= from && x.SelectedAt < to
                    ? (DateTime?)x.SelectedAt.Value.AddDays(back) : x.SelectedAt)
                // The deadline moves with the invitation that set it, so a round still open keeps
                // its fortnight and one long settled shows the date it actually ran out.
                .SetProperty(x => x.RespondBy, x => x.InvitedAt >= from && x.InvitedAt < to && x.RespondBy != null
                    ? (DateTime?)x.RespondBy.Value.AddDays(back) : x.RespondBy), ct);

        await db.ProposalAssignments.Where(a => a.Proposal.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssignedAt,
                a => a.AssignedAt >= from && a.AssignedAt < to ? a.AssignedAt.AddDays(back) : a.AssignedAt), ct);

        await db.EthicsDeclarations.Where(d => d.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DecidedAt,
                d => d.DecidedAt >= from && d.DecidedAt < to ? d.DecidedAt.AddDays(back) : d.DecidedAt), ct);

        await db.EthicsApprovals.Where(a => a.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.CreatedAt, a => a.CreatedAt >= from && a.CreatedAt < to ? a.CreatedAt.AddDays(back) : a.CreatedAt)
                .SetProperty(a => a.StepEnteredAt, a => a.StepEnteredAt != null && a.StepEnteredAt >= from && a.StepEnteredAt < to
                    ? (DateTime?)a.StepEnteredAt.Value.AddDays(back) : a.StepEnteredAt)
                .SetProperty(a => a.SupervisorDecisionAt, a => a.SupervisorDecisionAt != null && a.SupervisorDecisionAt >= from && a.SupervisorDecisionAt < to
                    ? (DateTime?)a.SupervisorDecisionAt.Value.AddDays(back) : a.SupervisorDecisionAt)
                .SetProperty(a => a.SupervisorDocumentsReviewedAt, a => a.SupervisorDocumentsReviewedAt != null && a.SupervisorDocumentsReviewedAt >= from && a.SupervisorDocumentsReviewedAt < to
                    ? (DateTime?)a.SupervisorDocumentsReviewedAt.Value.AddDays(back) : a.SupervisorDocumentsReviewedAt)
                .SetProperty(a => a.CoordinatorDecisionAt, a => a.CoordinatorDecisionAt != null && a.CoordinatorDecisionAt >= from && a.CoordinatorDecisionAt < to
                    ? (DateTime?)a.CoordinatorDecisionAt.Value.AddDays(back) : a.CoordinatorDecisionAt)
                .SetProperty(a => a.HeadOfDepartmentReviewedAt, a => a.HeadOfDepartmentReviewedAt != null && a.HeadOfDepartmentReviewedAt >= from && a.HeadOfDepartmentReviewedAt < to
                    ? (DateTime?)a.HeadOfDepartmentReviewedAt.Value.AddDays(back) : a.HeadOfDepartmentReviewedAt)
                .SetProperty(a => a.FinalDecisionAt, a => a.FinalDecisionAt != null && a.FinalDecisionAt >= from && a.FinalDecisionAt < to
                    ? (DateTime?)a.FinalDecisionAt.Value.AddDays(back) : a.FinalDecisionAt)
                .SetProperty(a => a.ApprovalDate, a => a.ApprovalDate != null && a.ApprovalDate >= from && a.ApprovalDate < to
                    ? (DateTime?)a.ApprovalDate.Value.AddDays(back) : a.ApprovalDate), ct);

        await db.EthicsDocuments.Where(d => d.EthicsApproval.PublicationContainerId == containerId
                                            && d.UploadedAt >= from && d.UploadedAt < to)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UploadedAt, d => d.UploadedAt.AddDays(back)), ct);

        await db.Publications.Where(p => p.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.CreatedAt, p => p.CreatedAt >= from && p.CreatedAt < to ? p.CreatedAt.AddDays(back) : p.CreatedAt)
                .SetProperty(p => p.UpdatedAt, p => p.UpdatedAt >= from && p.UpdatedAt < to ? p.UpdatedAt.AddDays(back) : p.UpdatedAt)
                .SetProperty(p => p.PublishedAt, p => p.PublishedAt != null && p.PublishedAt >= from && p.PublishedAt < to
                    ? (DateTime?)p.PublishedAt.Value.AddDays(back) : p.PublishedAt), ct);

        await db.PublicationVersions.Where(v => v.Publication.PublicationContainerId == containerId
                                                && v.UploadedAt >= from && v.UploadedAt < to)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.UploadedAt, v => v.UploadedAt.AddDays(back)), ct);

        await db.Reviews.Where(r => r.PublicationVersion.Publication.PublicationContainerId == containerId
                                    && r.ReviewedAt >= from && r.ReviewedAt < to)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedAt, r => r.ReviewedAt.AddDays(back)), ct);

        await db.Committees.Where(c => c.Publication.PublicationContainerId == containerId
                                       && c.CreatedAt >= from && c.CreatedAt < to)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddDays(back)), ct);

        await db.CommitteeMembers.Where(m => m.Committee.Publication.PublicationContainerId == containerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.InvitedAt, m => m.InvitedAt >= from && m.InvitedAt < to ? m.InvitedAt.AddDays(back) : m.InvitedAt)
                .SetProperty(m => m.DecidedAt, m => m.DecidedAt != null && m.DecidedAt >= from && m.DecidedAt < to
                    ? (DateTime?)m.DecidedAt.Value.AddDays(back) : m.DecidedAt), ct);

        // Both of these carry the container as a loose reference rather than a foreign key, which
        // is why they are matched by id rather than joined.
        await db.Notifications.Where(n => n.RelatedEntityId == containerId && n.CreatedAt >= from && n.CreatedAt < to)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, n => n.CreatedAt.AddDays(back)), ct);

        await db.AuditLogEntries.Where(e => e.EntityId == containerId && e.Timestamp >= from && e.Timestamp < to)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Timestamp, e => e.Timestamp.AddDays(back)), ct);
    }

    private static string Slug(string text)
    {
        var cleaned = new string(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        return string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries)).Trim('-');
    }
}
