using FluentAssertions;
using Moq;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

/// <summary>
/// The sweep that reminds people before a deadline and reports the two that pass without anything
/// else happening. Each pass is exercised directly against a real database, because what is worth
/// testing is which rows it picks and who it writes to, not the timer around them.
/// </summary>
public class DeadlineWatchServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<INotificationService> _notifications = new();

    /// <summary>Long enough periods that a test says which side of a deadline it means in days.</summary>
    private static readonly DeadlineSettingsDto Deadlines = new(14, 21, 30, 3, 5, 7);

    /// <summary>The ethics stage as it ships: supervisor reads first, coordinator after them.</summary>
    private static readonly EthicsWorkflowSettingsDto Workflow = new(false, false);

    public void Dispose() => _fixture.Dispose();

    // ---------- Supervisor response ----------

    [Fact]
    public async Task Warns_a_supervisor_once_however_many_proposals_are_waiting_on_them()
    {
        var (_, supervisor) = SeedProposalRound(respondBy: DateTime.UtcNow.AddDays(2), proposals: 3);

        await DeadlineWatchService.WatchProposalRoundsAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);

        _notifications.Verify(n => n.NotifyAsync(supervisor.Id, NotificationType.ProposalResponseDueSoon,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Every invitation is marked, not only the one the message counted, or the next sweep
        // would warn the same supervisor again about the ones it left behind.
        _fixture.Context.ProposalSupervisorSelections.Should().OnlyContain(s => s.DueSoonWarnedAt != null);
    }

    [Fact]
    public async Task Does_not_warn_a_supervisor_whose_answer_by_date_is_still_far_off()
    {
        SeedProposalRound(respondBy: DateTime.UtcNow.AddDays(10), proposals: 1);

        await DeadlineWatchService.WatchProposalRoundsAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);

        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Warns_a_supervisor_only_once_however_often_the_sweep_runs()
    {
        SeedProposalRound(respondBy: DateTime.UtcNow.AddDays(2), proposals: 2);

        await DeadlineWatchService.WatchProposalRoundsAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);
        await DeadlineWatchService.WatchProposalRoundsAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);

        _notifications.Verify(n => n.NotifyAsync(It.IsAny<Guid>(), NotificationType.ProposalResponseDueSoon,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sends_no_reminders_at_all_where_the_lead_time_is_zero()
    {
        SeedProposalRound(respondBy: DateTime.UtcNow.AddDays(1), proposals: 1);

        await DeadlineWatchService.WatchProposalRoundsAsync(
            _fixture.Context, _notifications.Object, Deadlines with { SupervisorResponseWarningDays = 0 },
            CancellationToken.None);

        _notifications.VerifyNoOtherCalls();
    }

    // ---------- Ethics review ----------

    [Fact]
    public async Task Tells_the_coordinator_when_an_ethics_review_has_run_out_of_time()
    {
        var (container, approval) = SeedEthicsApproval(
            EthicsStatus.PendingSupervisorDecision, enteredAt: DateTime.UtcNow.AddDays(-30));

        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines, Workflow, CancellationToken.None);

        _notifications.Verify(n => n.NotifyAsync(container.CoordinatorId, NotificationType.EthicsReviewOverdue,
            It.IsAny<string>(),
            It.Is<string>(message => message.Contains("supervisor")),
            It.IsAny<string?>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);

        _fixture.Context.Entry(approval).Reload();
        approval.OverdueReportedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reports_an_overdue_ethics_review_once_however_often_the_sweep_runs()
    {
        SeedEthicsApproval(EthicsStatus.PendingSupervisorDecision, enteredAt: DateTime.UtcNow.AddDays(-30));

        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines, Workflow, CancellationToken.None);
        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines, Workflow, CancellationToken.None);

        _notifications.Verify(n => n.NotifyAsync(It.IsAny<Guid>(), NotificationType.EthicsReviewOverdue,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Warns_the_supervisor_rather_than_the_coordinator_before_an_ethics_review_is_due()
    {
        // Eighteen days into a twenty-one-day period, warned five days ahead: inside the window,
        // and the deadline has not passed.
        var (container, _) = SeedEthicsApproval(
            EthicsStatus.PendingSupervisorDecision, enteredAt: DateTime.UtcNow.AddDays(-18));

        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines, Workflow, CancellationToken.None);

        _notifications.Verify(n => n.NotifyAsync(container.AssignedSupervisorId!.Value,
            NotificationType.EthicsReviewDueSoon, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);

        _notifications.Verify(n => n.NotifyAsync(container.CoordinatorId, It.IsAny<NotificationType>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Leaves_an_ethics_review_alone_while_it_is_well_inside_its_time()
    {
        SeedEthicsApproval(EthicsStatus.PendingSupervisorDecision, enteredAt: DateTime.UtcNow.AddDays(-2));

        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines, Workflow, CancellationToken.None);

        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Says_nothing_about_an_ethics_review_that_has_been_decided()
    {
        var (_, approval) = SeedEthicsApproval(
            EthicsStatus.Verified, enteredAt: DateTime.UtcNow.AddDays(-90));
        approval.FinalDecisionAt = DateTime.UtcNow.AddDays(-80);
        _fixture.Context.SaveChanges();

        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines, Workflow, CancellationToken.None);

        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Watches_nothing_where_the_ethics_deadline_is_switched_off()
    {
        SeedEthicsApproval(EthicsStatus.PendingSupervisorDecision, enteredAt: DateTime.UtcNow.AddDays(-365));

        await DeadlineWatchService.WatchEthicsAsync(
            _fixture.Context, _notifications.Object, Deadlines with { EthicsReviewDays = 0 }, Workflow,
            CancellationToken.None);

        _notifications.VerifyNoOtherCalls();
    }

    // ---------- Committee review ----------

    [Fact]
    public async Task Names_the_committee_members_who_have_not_decided_when_the_time_has_gone()
    {
        var (container, _, undecided) = SeedCommittee(
            createdAt: DateTime.UtcNow.AddDays(-40), undecidedMembers: 2, decidedMembers: 1);

        await DeadlineWatchService.WatchCommitteesAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);

        _notifications.Verify(n => n.NotifyAsync(container.CoordinatorId, NotificationType.CommitteeReviewOverdue,
            It.IsAny<string>(),
            It.Is<string>(message => undecided.All(u => message.Contains(u.LastName!))),
            It.IsAny<string?>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Warns_only_the_committee_members_who_still_owe_a_decision()
    {
        var (_, decided, undecided) = SeedCommittee(
            createdAt: DateTime.UtcNow.AddDays(-25), undecidedMembers: 2, decidedMembers: 1);

        await DeadlineWatchService.WatchCommitteesAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);

        foreach (var member in undecided)
        {
            _notifications.Verify(n => n.NotifyAsync(member.Id, NotificationType.CommitteeReviewDueSoon,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        _notifications.Verify(n => n.NotifyAsync(decided.Id, It.IsAny<NotificationType>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Says_nothing_about_a_committee_where_everybody_has_decided()
    {
        SeedCommittee(createdAt: DateTime.UtcNow.AddDays(-40), undecidedMembers: 0, decidedMembers: 2);

        await DeadlineWatchService.WatchCommitteesAsync(
            _fixture.Context, _notifications.Object, Deadlines, CancellationToken.None);

        _notifications.VerifyNoOtherCalls();
    }

    // ---------- Seeding ----------

    private (PublicationContainer Container, ApplicationUser Supervisor) SeedProposalRound(
        DateTime respondBy, int proposals)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        for (var i = 0; i < proposals; i++)
        {
            var proposal = TestDataBuilder.Proposal(
                _fixture.Context, container, status: ProposalStatus.Submitted);

            _fixture.Context.ProposalSupervisorSelections.Add(new ProposalSupervisorSelection
            {
                ProposalId = proposal.Id,
                SupervisorId = supervisor.Id,
                RespondBy = respondBy
            });
        }

        _fixture.Context.SaveChanges();
        return (container, supervisor);
    }

    private (PublicationContainer Container, EthicsApproval Approval) SeedEthicsApproval(
        EthicsStatus status, DateTime enteredAt)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, coordinator, supervisor, PipelineStage.EthicsApproval);

        var approval = new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = status,
            StepEnteredAt = enteredAt
        };
        _fixture.Context.EthicsApprovals.Add(approval);
        _fixture.Context.SaveChanges();

        return (container, approval);
    }

    private (PublicationContainer Container, ApplicationUser Decided, IReadOnlyList<ApplicationUser> Undecided)
        SeedCommittee(DateTime createdAt, int undecidedMembers, int decidedMembers)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, coordinator, supervisor, PipelineStage.ResearchPaper);

        var publication = new Publication
        {
            PublicationContainerId = container.Id,
            Title = "T",
            Abstract = "A",
            Status = PublicationStatus.UnderReview
        };
        _fixture.Context.Publications.Add(publication);
        _fixture.Context.SaveChanges();

        var committee = new Committee
        {
            PublicationId = publication.Id,
            Status = CommitteeStatus.InReview,
            MinApprovalsRequired = 1,
            CreatedByUserId = coordinator.Id,
            CreatedAt = createdAt
        };
        _fixture.Context.Committees.Add(committee);
        _fixture.Context.SaveChanges();

        var undecided = new List<ApplicationUser>();
        ApplicationUser? decided = null;

        for (var i = 0; i < undecidedMembers; i++)
        {
            var member = TestDataBuilder.User(_fixture.Context);

            // A surname of their own, so a test that the message names the right people cannot
            // pass on the builder's default of everyone being called the same thing.
            member.LastName = $"Undecided{i}";
            _fixture.Context.SaveChanges();

            undecided.Add(member);
            _fixture.Context.CommitteeMembers.Add(new CommitteeMember
            {
                CommitteeId = committee.Id,
                UserId = member.Id,
                RoleType = CommitteeMemberRoleType.Reviewer
            });
        }

        for (var i = 0; i < decidedMembers; i++)
        {
            decided = TestDataBuilder.User(_fixture.Context);
            _fixture.Context.CommitteeMembers.Add(new CommitteeMember
            {
                CommitteeId = committee.Id,
                UserId = decided.Id,
                RoleType = CommitteeMemberRoleType.Reviewer,
                Decision = CommitteeMemberDecision.Approve,
                DecidedAt = DateTime.UtcNow
            });
        }

        _fixture.Context.SaveChanges();
        return (container, decided!, undecided);
    }
}
