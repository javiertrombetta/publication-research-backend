using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class ProposalServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<ISystemSettingService> _settingService = new();
    private readonly Mock<INotificationService> _notificationService = new();

    /// <summary>
    /// A real provider over the test database rather than a mock: the service reads the expected
    /// supervisor response time through it, and a stub returning zero would quietly test a default
    /// nobody ships. With no rows written it falls back to the same value the application does.
    /// </summary>
    private readonly SystemSettingsProvider _settings;

    private readonly ProposalService _sut;

    public ProposalServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
        _settings = new SystemSettingsProvider(_fixture.Context, new MemoryCache(new MemoryCacheOptions()));
        // The stages as they ship: ethics first, then the research paper.
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(true, true, true, true));

        // The proposals stage as it ships: supervisors say which they are willing to take on.
        _settingService.Setup(s => s.GetProposalSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProposalSettingsDto(1, 3, SupervisorsExpressInterest: true));

        _sut = new ProposalService(_fixture.Context, _accessService.Object, _auditService.Object,
            _notificationService.Object, _settings, _settingService.Object, new DecisionCommentPolicy(new SystemSettingsProvider(_fixture.Context, new MemoryCache(new MemoryCacheOptions()))));
    }

    public void Dispose() => _fixture.Dispose();

    private (ApplicationUser Student, ApplicationUser Coordinator, PublicationContainer Container) SeedContainer()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);
        return (student, coordinator, container);
    }

    [Fact]
    public async Task CreateAsync_creates_draft_proposal_for_owner()
    {
        var (student, _, container) = SeedContainer();

        var result = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("Title", "Abstract"));

        result.Status.Should().Be(ProposalStatus.Draft.ToString());
    }

    [Fact]
    public async Task CreateAsync_denies_non_owner()
    {
        var (_, _, container) = SeedContainer();
        var stranger = Guid.NewGuid();

        var act = () => _sut.CreateAsync(container.Id, stranger, new SaveProposalRequest("Title", "Abstract"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateAsync_rejects_when_proposals_already_locked()
    {
        var (student, _, container) = SeedContainer();
        _fixture.Context.ResearchProposals.Add(new ResearchProposal
        {
            PublicationContainerId = container.Id, Title = "Existing", Abstract = "A", Status = ProposalStatus.Submitted
        });
        _fixture.Context.SaveChanges();

        var act = () => _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("New", "Abstract"));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateAsync_edits_draft_proposal()
    {
        var (student, _, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("Old", "Old abstract"));

        var updated = await _sut.UpdateAsync(proposal.Id, student.Id, new SaveProposalRequest("New", "New abstract"));

        updated.Title.Should().Be("New");
    }

    [Fact]
    public async Task UpdateAsync_rejects_locked_proposal()
    {
        var (student, _, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("Title", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var act = () => _sut.UpdateAsync(proposal.Id, student.Id, new SaveProposalRequest("New", "New"));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task FinishSubmissionAsync_locks_all_drafts()
    {
        var (student, _, container) = SeedContainer();
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract A"));
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("B", "Abstract B"));

        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var proposals = await _sut.GetByContainerAsync(container.Id, student.Id);
        proposals.Should().OnlyContain(p => p.Status == ProposalStatus.Submitted.ToString() && p.SubmittedAt != null);
    }

    [Fact]
    public async Task FinishSubmissionAsync_requires_at_least_one_draft()
    {
        var (student, _, container) = SeedContainer();

        var act = () => _sut.FinishSubmissionAsync(container.Id, student.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    /// <summary>
    /// Pressing the button twice, or going back to the form after sending, used to be answered with
    /// the count check below it: a student with three proposals sitting with their supervisor was
    /// told they had written none.
    /// </summary>
    [Fact]
    public async Task FinishSubmissionAsync_says_so_when_the_proposals_have_already_gone()
    {
        var (student, _, container) = SeedContainer();
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract A"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var act = () => _sut.FinishSubmissionAsync(container.Id, student.Id);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage("These research proposals have already been sent.");
    }

    [Fact]
    public async Task RequestNewSubmissionAsync_rejects_existing_proposals_and_notifies_student()
    {
        var (student, coordinator, container) = SeedContainer();
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        await _sut.RequestNewSubmissionAsync(container.Id, "Please resubmit", coordinator.Id);

        var proposals = await _sut.GetByContainerAsync(container.Id, student.Id);
        proposals.Should().OnlyContain(p => p.Status == ProposalStatus.Rejected.ToString());
        _notificationService.Verify(n => n.NotifyAsync(
            student.Id, NotificationType.NewProposalSubmissionRequested,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Holding the Coordinator role is not the same as coordinating this student. Without the
    /// check, any coordinator could throw away the proposals of somebody in another department.
    /// </summary>
    [Fact]
    public async Task RequestNewSubmissionAsync_refuses_a_coordinator_from_somewhere_else()
    {
        var (student, _, container) = SeedContainer();
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var act = () => _sut.RequestNewSubmissionAsync(container.Id, "Please resubmit", Guid.NewGuid());

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    /// <summary>An administrator may, which is the exemption the acting-as-admin flag carries.</summary>
    [Fact]
    public async Task RequestNewSubmissionAsync_allows_an_administrator()
    {
        var (student, _, container) = SeedContainer();
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        await _sut.RequestNewSubmissionAsync(container.Id, "Please resubmit", Guid.NewGuid(), actingAsAdmin: true);

        var proposals = await _sut.GetByContainerAsync(container.Id, student.Id);
        proposals.Should().OnlyContain(p => p.Status == ProposalStatus.Rejected.ToString());
    }

    [Fact]
    public async Task GetPendingForCoordinatorAsync_returns_only_submitted_uninvited_proposals_for_that_coordinator()
    {
        var (student, coordinator, container) = SeedContainer();
        var pending = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("Pending", "A"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var otherCoordinator = TestDataBuilder.User(_fixture.Context);
        var result = (await _sut.GetPendingForCoordinatorAsync(coordinator.Id, new PageRequest())).Items;
        var otherResult = (await _sut.GetPendingForCoordinatorAsync(otherCoordinator.Id, new PageRequest())).Items;

        result.Should().ContainSingle(p => p.Id == pending.Id);
        otherResult.Should().BeEmpty();
    }

    [Fact]
    public async Task SendToSupervisorsAsync_invites_supervisors_and_notifies_each_once()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor1 = Supervisor();
        var supervisor2 = Supervisor();

        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor1.Id, supervisor2.Id], "Please review"), coordinator.Id);

        var invited = await _fixture.Context.ProposalSupervisorSelections.Where(s => s.ProposalId == proposal.Id).ToListAsync();
        invited.Should().HaveCount(2);
        invited.Should().OnlyContain(s => !s.IsSelected);

        _notificationService.Verify(n => n.NotifyAsync(
            supervisor1.Id, NotificationType.ProposalsAwaitingEvaluation, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.NotifyAsync(
            supervisor2.Id, NotificationType.ProposalsAwaitingEvaluation, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The first handover in the workflow, and the only one that used to be silent. Every ethics
    /// step and every step of the paper tells whoever is next; a student finishing their proposals
    /// told nobody, and the coordinator found out by happening to open the dispatch screen.
    /// </summary>
    [Fact]
    public async Task FinishSubmissionAsync_tells_the_coordinator_there_is_a_round_to_send_out()
    {
        var (student, coordinator, container) = SeedContainer();
        await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));

        _fixture.Context.ChangeTracker.Clear();
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        _notificationService.Verify(n => n.NotifyAsync(
            coordinator.Id, NotificationType.ProposalsAwaitingEvaluation, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>And the answer coming back, which makes it the coordinator's turn to allocate.</summary>
    [Fact]
    public async Task SelectAsFeasibleAsync_tells_the_coordinator_a_supervisor_has_answered()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor = Supervisor();
        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "Please review", DateTime.UtcNow.AddDays(7)),
            coordinator.Id);

        // Forgotten, so what the method reads has to come from its own query. Everything the test
        // touched is still tracked otherwise, and EF fills a navigation in from what it is already
        // holding: the test would pass with the Include missing and fail in front of a real
        // request, which arrives on a context that knows nothing.
        _fixture.Context.ChangeTracker.Clear();

        await _sut.SelectAsFeasibleAsync(proposal.Id, supervisor.Id, new SupervisorSelectionRequest("I could take this."));

        _notificationService.Verify(n => n.NotifyAsync(
            coordinator.Id, NotificationType.ProposalsAwaitingEvaluation, "A supervisor has answered", It.IsAny<string>(),
            It.IsAny<string?>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A round is one decision with one explanation, so it is recorded once. Written inside the
    /// loop over the proposals, the coordinator's paragraph landed on the publication's history
    /// three times in a row, and a reader months later cannot tell that from three separate
    /// dispatches that happened to be worded the same way.
    /// </summary>
    [Fact]
    public async Task SendToSupervisorsAsync_records_the_round_once_however_many_proposals_it_holds()
    {
        var (student, coordinator, container) = SeedContainer();
        var first = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract A"));
        var second = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("B", "Abstract B"));
        var third = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("C", "Abstract C"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor = Supervisor();

        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([first.Id, second.Id, third.Id], [supervisor.Id], "All three are worth a look."),
            coordinator.Id);

        _auditService.Verify(a => a.LogActivityAsync(
            container.Id, coordinator.Id, "ProposalsSentToSupervisors", "All three are worth a look.",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>()), Times.Once);
    }

    /// <summary>
    /// The screens only ever offer an available Supervisor, and nothing made that true of the
    /// request. An id belonging to a student, an administrator, a disabled account or to nobody at
    /// all was carried out: the last failed on the foreign key and surfaced as a server error, the
    /// rest succeeded and put the work in the hands of somebody who is not a supervisor.
    /// </summary>
    [Theory]
    [InlineData("student")]
    [InlineData("disabled")]
    [InlineData("no role at all")]
    [InlineData("unavailable")]
    [InlineData("nobody")]
    public async Task SendToSupervisorsAsync_refuses_anybody_who_cannot_supervise(string who)
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var target = who switch
        {
            "nobody" => Guid.NewGuid(),
            _ => SeedWhoCannotSupervise(who).Id
        };

        var act = () => _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [target], "Please review", DateTime.UtcNow.AddDays(7)),
            coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();

        (await _fixture.Context.ProposalSupervisorSelections
            .AnyAsync(sel => sel.ProposalId == proposal.Id)).Should().BeFalse();
    }

    /// <summary>
    /// The same guard on the other route in. Where the institution appoints directly there is no
    /// offer to check against, so this is the only thing between the request and a publication
    /// supervised by a student.
    /// </summary>
    [Fact]
    public async Task AssignSupervisorAsync_refuses_somebody_who_cannot_supervise()
    {
        _fixture.Context.SystemSettings.Add(new SystemSetting
        {
            Key = SettingKeys.ProposalsSupervisorsExpressInterest,
            Value = "false"
        });
        await _fixture.Context.SaveChangesAsync();

        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var act = () => _sut.AssignSupervisorAsync(proposal.Id,
            new AssignSupervisorRequest(SeedWhoCannotSupervise("student").Id, "Appointing them."), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();

        (await _fixture.Context.PublicationContainers.FirstAsync(c => c.Id == container.Id))
            .AssignedSupervisorId.Should().BeNull();
    }

    [Fact]
    public async Task SendToSupervisorsAsync_still_accepts_an_available_supervisor()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor = Supervisor();

        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "Please review", DateTime.UtcNow.AddDays(7)),
            coordinator.Id);

        (await _fixture.Context.ProposalSupervisorSelections
            .CountAsync(sel => sel.ProposalId == proposal.Id)).Should().Be(1);
    }

    /// <summary>
    /// An account that can actually take supervision on. The role matters now: sending proposals
    /// to somebody who does not hold it is refused, which is the point, and a fixture that leaves
    /// it off is describing a coordinator picking a name the screens never offered.
    /// </summary>
    private ApplicationUser Supervisor()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, user, RoleNames.Supervisor);
        return user;
    }

    private ApplicationUser SeedWhoCannotSupervise(string who)
    {
        var user = TestDataBuilder.User(_fixture.Context,
            status: who == "disabled" ? UserStatus.Disabled : UserStatus.Enabled);

        if (who == "student") TestDataBuilder.GrantRole(_fixture.Context, user, RoleNames.Student);
        if (who == "disabled" || who == "unavailable") TestDataBuilder.GrantRole(_fixture.Context, user, RoleNames.Supervisor);

        if (who == "unavailable")
        {
            user.IsAvailable = false;
            _fixture.Context.SaveChanges();
        }

        return user;
    }

    [Fact]
    public async Task SendToSupervisorsAsync_goes_through_without_a_message_when_the_institution_asks_for_none()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);
        var supervisor = Supervisor();

        // No row written, so the decision stands at its default, which is optional.
        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], string.Empty), coordinator.Id);

        (await _fixture.Context.ProposalSupervisorSelections.CountAsync(s => s.ProposalId == proposal.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task SendToSupervisorsAsync_asks_for_a_message_when_the_institution_requires_one()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);
        var supervisor = Supervisor();

        _fixture.Context.SystemSettings.Add(new SystemSetting
        {
            Key = DecisionPoints.SettingKeyFor(DecisionPoints.ProposalSendToSupervisors),
            Value = "true"
        });
        await _fixture.Context.SaveChangesAsync();

        var act = () => _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "   "), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendToSupervisorsAsync_rejects_coordinator_who_does_not_own_the_container()
    {
        var (student, _, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var impostor = TestDataBuilder.User(_fixture.Context);
        var supervisor = Supervisor();

        var act = () => _sut.SendToSupervisorsAsync(new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "x"), impostor.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SelectAsFeasibleAsync_marks_selection_and_updates_proposal_status()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);
        var supervisor = Supervisor();
        await _sut.SendToSupervisorsAsync(new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "x"), coordinator.Id);

        await _sut.SelectAsFeasibleAsync(proposal.Id, supervisor.Id, new SupervisorSelectionRequest("Happy to supervise"));

        var selections = await _sut.GetSelectionsAsync(proposal.Id, coordinator.Id);
        selections.Should().ContainSingle(s => s.SupervisorId == supervisor.Id && s.IsSelected);

        var proposals = await _sut.GetByContainerAsync(container.Id, student.Id);
        proposals.Single().Status.Should().Be(ProposalStatus.SelectedBySupervisor.ToString());
    }

    [Fact]
    public async Task SelectAsFeasibleAsync_rejects_supervisor_who_was_not_invited()
    {
        var (student, _, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        var uninvitedSupervisor = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.SelectAsFeasibleAsync(proposal.Id, uninvitedSupervisor.Id, new SupervisorSelectionRequest(null));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task AssignSupervisorAsync_assigns_supervisor_advances_pipeline_and_rejects_siblings()
    {
        var (student, coordinator, container) = SeedContainer();
        var chosen = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("Chosen", "A"));
        var other = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("Other", "B"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor = Supervisor();
        await _sut.SendToSupervisorsAsync(new SendToSupervisorsRequest([chosen.Id], [supervisor.Id], "x"), coordinator.Id);
        await _sut.SelectAsFeasibleAsync(chosen.Id, supervisor.Id, new SupervisorSelectionRequest(null));

        await _sut.AssignSupervisorAsync(chosen.Id, new AssignSupervisorRequest(supervisor.Id, "Great fit"), coordinator.Id);

        var updatedContainer = await _fixture.Context.PublicationContainers.FindAsync(container.Id);
        updatedContainer!.AssignedSupervisorId.Should().Be(supervisor.Id);
        updatedContainer.CurrentPipeline.Should().Be(PipelineStage.EthicsApproval);

        var proposals = await _sut.GetByContainerAsync(container.Id, student.Id);
        proposals.Single(p => p.Id == chosen.Id).Status.Should().Be(ProposalStatus.Assigned.ToString());
        proposals.Single(p => p.Id == other.Id).Status.Should().Be(ProposalStatus.Rejected.ToString());

        _notificationService.Verify(n => n.NotifyAsync(
            supervisor.Id, NotificationType.ProposalAccepted, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.NotifyAsync(
            student.Id, NotificationType.ProposalAccepted, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignSupervisorAsync_rejects_supervisor_who_was_not_selected()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);
        var supervisor = Supervisor();

        var act = () => _sut.AssignSupervisorAsync(proposal.Id, new AssignSupervisorRequest(supervisor.Id, "x"), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Assigning_a_supervisor_opens_the_paper_first_where_the_institution_runs_it_that_way()
    {
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(true, true, true, EthicsBeforePaper: false));

        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor = Supervisor();
        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "Please review"), coordinator.Id);
        await _sut.SelectAsFeasibleAsync(proposal.Id, supervisor.Id, new SupervisorSelectionRequest(null));
        await _sut.AssignSupervisorAsync(proposal.Id, new AssignSupervisorRequest(supervisor.Id, "Assigned"), coordinator.Id);

        // Proposals are still first; what follows them is the stage this institution runs next.
        (await _fixture.Context.PublicationContainers.FindAsync(container.Id))!.CurrentPipeline
            .Should().Be(PipelineStage.ResearchPaper);
    }

    // ---------- An administrator correcting which proposal a publication runs on ----------

    /// <summary>
    /// A publication past its proposals stage: one proposal assigned, the rest turned down, and a
    /// paper on it at whatever status the test needs.
    /// </summary>
    private (PublicationContainer Container, ResearchProposal Assigned, ResearchProposal Other) SeedAssignedWithPaper(
        PublicationStatus paperStatus)
    {
        var (student, coordinator, container) = SeedContainer();
        var supervisor = Supervisor();
        container.AssignedSupervisorId = supervisor.Id;
        container.CurrentPipeline = PipelineStage.ResearchPaper;

        var assigned = TestDataBuilder.Proposal(_fixture.Context, container, "The one it runs on", ProposalStatus.Assigned);
        var other = TestDataBuilder.Proposal(_fixture.Context, container, "The one it does not", ProposalStatus.Rejected);

        _fixture.Context.Publications.Add(new Publication
        {
            PublicationContainerId = container.Id, Title = "T", Abstract = "A", Status = paperStatus
        });
        _fixture.Context.SaveChanges();

        _ = student; _ = coordinator;
        return (container, assigned, other);
    }

    [Fact]
    public async Task An_administrator_can_settle_a_publication_on_a_different_proposal()
    {
        var (container, assigned, other) = SeedAssignedWithPaper(PublicationStatus.UnderReview);

        await _sut.ChangeAssignedProposalAsync(other.Id, "The student and supervisor agreed on this one.", Guid.NewGuid());

        // Exactly one assigned and the rest turned down, which is the shape the coordinator's own
        // assignment leaves behind.
        var after = await _fixture.Context.ResearchProposals
            .Where(p => p.PublicationContainerId == container.Id).ToListAsync();

        after.Single(p => p.Id == other.Id).Status.Should().Be(ProposalStatus.Assigned);
        after.Single(p => p.Id == assigned.Id).Status.Should().Be(ProposalStatus.Rejected);
        after.Count(p => p.Status == ProposalStatus.Assigned).Should().Be(1);
    }

    [Fact]
    public async Task Everyone_working_on_the_publication_is_told_the_proposal_changed()
    {
        var (container, _, other) = SeedAssignedWithPaper(PublicationStatus.UnderReview);

        await _sut.ChangeAssignedProposalAsync(other.Id, "Corrected.", Guid.NewGuid());

        foreach (var person in new[] { container.StudentId, container.CoordinatorId, container.AssignedSupervisorId!.Value })
        {
            _notificationService.Verify(n => n.NotifyAsync(person, It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), container.Id,
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Theory]
    [InlineData(PublicationStatus.Accepted)]
    [InlineData(PublicationStatus.Published)]
    public async Task The_proposal_cannot_be_changed_once_the_paper_has_been_accepted(PublicationStatus settled)
    {
        var (_, _, other) = SeedAssignedWithPaper(settled);

        var act = () => _sut.ChangeAssignedProposalAsync(other.Id, "Too late.", Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Theory]
    [InlineData(PublicationStatus.Accepted)]
    [InlineData(PublicationStatus.Published)]
    public async Task A_new_round_cannot_be_asked_for_once_the_paper_has_been_accepted(PublicationStatus settled)
    {
        var (container, _, _) = SeedAssignedWithPaper(settled);

        // It would turn down the proposal the accepted paper was written from, and nobody could
        // then say what the paper had been approved to be about.
        var act = () => _sut.RequestNewSubmissionAsync(container.Id, "Start again.", Guid.NewGuid(), actingAsAdmin: true);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task The_proposal_already_being_run_on_cannot_be_assigned_again()
    {
        var (_, assigned, _) = SeedAssignedWithPaper(PublicationStatus.UnderReview);

        var act = () => _sut.ChangeAssignedProposalAsync(assigned.Id, "No change.", Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Nothing_can_be_settled_on_before_the_coordinator_has_assigned_one()
    {
        var (_, _, container) = SeedContainer();
        var proposal = TestDataBuilder.Proposal(_fixture.Context, container, "Untouched", ProposalStatus.Submitted);

        // Doing it here would settle the publication on a proposal without naming a supervisor,
        // which is half of the coordinator's act.
        var act = () => _sut.ChangeAssignedProposalAsync(proposal.Id, "Not yet.", Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
