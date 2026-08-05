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

        var supervisor1 = TestDataBuilder.User(_fixture.Context);
        var supervisor2 = TestDataBuilder.User(_fixture.Context);

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

    [Fact]
    public async Task SendToSupervisorsAsync_goes_through_without_a_message_when_the_institution_asks_for_none()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);
        var supervisor = TestDataBuilder.User(_fixture.Context);

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
        var supervisor = TestDataBuilder.User(_fixture.Context);

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
        var supervisor = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.SendToSupervisorsAsync(new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "x"), impostor.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SelectAsFeasibleAsync_marks_selection_and_updates_proposal_status()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);
        var supervisor = TestDataBuilder.User(_fixture.Context);
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

        var supervisor = TestDataBuilder.User(_fixture.Context);
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
        var supervisor = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.AssignSupervisorAsync(proposal.Id, new AssignSupervisorRequest(supervisor.Id, "x"), coordinator.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DeferToNextCycleAsync_marks_open_proposals_as_deferred()
    {
        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        await _sut.DeferToNextCycleAsync(container.Id, "No match this cycle", coordinator.Id);

        var proposals = await _sut.GetByContainerAsync(container.Id, student.Id);
        proposals.Single(p => p.Id == proposal.Id).Status.Should().Be(ProposalStatus.DeferredToNextCycle.ToString());
    }

    [Fact]
    public async Task Assigning_a_supervisor_opens_the_paper_first_where_the_institution_runs_it_that_way()
    {
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(true, true, true, EthicsBeforePaper: false));

        var (student, coordinator, container) = SeedContainer();
        var proposal = await _sut.CreateAsync(container.Id, student.Id, new SaveProposalRequest("A", "Abstract"));
        await _sut.FinishSubmissionAsync(container.Id, student.Id);

        var supervisor = TestDataBuilder.User(_fixture.Context);
        await _sut.SendToSupervisorsAsync(
            new SendToSupervisorsRequest([proposal.Id], [supervisor.Id], "Please review"), coordinator.Id);
        await _sut.SelectAsFeasibleAsync(proposal.Id, supervisor.Id, new SupervisorSelectionRequest(null));
        await _sut.AssignSupervisorAsync(proposal.Id, new AssignSupervisorRequest(supervisor.Id, "Assigned"), coordinator.Id);

        // Proposals are still first; what follows them is the stage this institution runs next.
        (await _fixture.Context.PublicationContainers.FindAsync(container.Id))!.CurrentPipeline
            .Should().Be(PipelineStage.ResearchPaper);
    }
}
