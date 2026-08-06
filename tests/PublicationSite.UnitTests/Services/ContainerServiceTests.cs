using FluentAssertions;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Entities;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.UnitTests.Services;

public class ContainerServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IDepartmentService> _departmentService = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<ISystemSettingService> _settingService = new();
    private readonly ContainerService _sut;

    public ContainerServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);

        // Creating a container snapshots the committee rules onto it, so they have to answer.
        _settingService.Setup(s => s.GetCommitteeSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitteeSettingsDto(2, 1, 2, RoleNames.CommitteeEligible, [], RoleNames.CommitteeEligible));

        // Every listing asks whether this institution runs the Head of Department step, because
        // the answer decides whose turn a publication says it is on.
        _settingService.Setup(s => s.GetEthicsWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EthicsWorkflowSettingsDto(true, true, true, true));

        // The research paper stage as it ships: all three readings run.
        _settingService.Setup(s => s.GetPaperWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperWorkflowSettingsDto(true, true, true, true));

        _sut = new ContainerService(_fixture.ServiceContext, _departmentService.Object, _accessService.Object,
            _auditService.Object, _notificationService.Object, _settingService.Object);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CreateAsync_auto_assigns_coordinator_from_department_selection()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);

        _departmentService.Setup(d => d.SelectCoordinatorForDepartmentAsync(department.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinator.Id);

        var result = await _sut.CreateAsync(student.Id);

        result.CoordinatorId.Should().Be(coordinator.Id);
        result.CurrentPipeline.Should().Be((int)PipelineStage.ResearchProposals);
        result.Status.Should().Be(ContainerStatus.InProgress.ToString());
    }

    [Fact]
    public async Task CreateAsync_allows_a_student_to_run_several_containers_at_once()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var existing = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        _departmentService.Setup(d => d.SelectCoordinatorForDepartmentAsync(department.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(coordinator.Id);

        var result = await _sut.CreateAsync(student.Id);

        result.Id.Should().NotBe(existing.Id);

        var mine = (await _sut.GetMineAsync(student.Id, new PageRequest())).Items;
        mine.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMineAsync_returns_empty_when_the_student_has_not_started_any()
    {
        var student = TestDataBuilder.User(_fixture.Context);

        var mine = (await _sut.GetMineAsync(student.Id, new PageRequest())).Items;

        mine.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteOwnAsync_discards_a_container_that_has_no_proposals()
    {
        var student = TestDataBuilder.User(_fixture.Context);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        await _sut.DeleteOwnAsync(container.Id, student.Id);

        (await _sut.GetMineAsync(student.Id, new PageRequest())).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteOwnAsync_throws_once_the_container_holds_a_proposal()
    {
        var student = TestDataBuilder.User(_fixture.Context);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);
        TestDataBuilder.Proposal(_fixture.Context, container);

        var act = () => _sut.DeleteOwnAsync(container.Id, student.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DeleteOwnAsync_throws_when_the_container_belongs_to_another_student()
    {
        var owner = TestDataBuilder.User(_fixture.Context);
        var otherStudent = TestDataBuilder.User(_fixture.Context);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, owner, coordinator);

        var act = () => _sut.DeleteOwnAsync(container.Id, otherStudent.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateAsync_throws_when_student_has_no_profile()
    {
        var student = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.CreateAsync(student.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AssignCoordinatorManuallyAsync_creates_container_when_none_exists()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = CoordinatorOf(department);

        var result = await _sut.AssignCoordinatorManuallyAsync(
            new AssignCoordinatorRequest(student.Id, coordinator.Id, "Manual assignment"), Guid.NewGuid());

        result.StudentId.Should().Be(student.Id);
        result.CoordinatorId.Should().Be(coordinator.Id);
    }

    /// <summary>
    /// Somebody who holds the coordinator role and is posted to a department. Opening a publication
    /// makes the same appointment the assignments screen makes, and asks the same two things of it.
    /// </summary>
    private ApplicationUser CoordinatorOf(Department department)
    {
        var coordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, coordinator, RoleNames.Coordinator);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, coordinator, department);
        return coordinator;
    }

    /// <summary>
    /// A student who already has a publication gets a second one, not a changed one. Students run
    /// more than one publication, so having one already is no reason to refuse, and this endpoint
    /// only ever opens them.
    /// </summary>
    [Fact]
    public async Task AssignCoordinatorManuallyAsync_opens_another_container_for_a_student_who_has_one()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var originalCoordinator = TestDataBuilder.User(_fixture.Context);
        var existing = TestDataBuilder.Container(_fixture.Context, student, originalCoordinator);

        var newCoordinator = CoordinatorOf(department);

        var result = await _sut.AssignCoordinatorManuallyAsync(
            new AssignCoordinatorRequest(student.Id, newCoordinator.Id, "A second publication"), Guid.NewGuid());

        result.Id.Should().NotBe(existing.Id);
        result.CoordinatorId.Should().Be(newCoordinator.Id);

        // And the first one is left exactly as it was, rather than quietly moved to the new person.
        _fixture.Context.PublicationContainers.Single(c => c.Id == existing.Id)
            .CoordinatorId.Should().Be(originalCoordinator.Id);
    }

    /// <summary>
    /// Moving a publication to another coordinator belongs to the assignments endpoint, which
    /// checks the role and the department and records the reason. This one used to accept a
    /// container id and do the same move with none of those checks.
    /// </summary>
    [Fact]
    public async Task AssignCoordinatorManuallyAsync_refuses_a_container_id()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var originalCoordinator = TestDataBuilder.User(_fixture.Context);
        var existing = TestDataBuilder.Container(_fixture.Context, student, originalCoordinator);

        var newCoordinator = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.AssignCoordinatorManuallyAsync(
            new AssignCoordinatorRequest(student.Id, newCoordinator.Id, "Reassigning", existing.Id),
            Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetByIdAsync_checks_access_before_returning()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        var requester = Guid.NewGuid();
        await _sut.GetByIdAsync(container.Id, requester);

        _accessService.Verify(a => a.EnsureAccessAsync(container.Id, requester), Times.Once);
    }

    [Fact]
    public async Task ReassignAsync_changes_both_and_tells_whoever_now_has_the_work()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, supervisor);

        var newCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, newCoordinator, RoleNames.Coordinator);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, newCoordinator, department);
        var newSupervisor = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, newSupervisor, RoleNames.Supervisor);

        var admin = Guid.NewGuid();
        var result = await _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(newCoordinator.Id, newSupervisor.Id, "The coordinator is on leave."), admin);

        result.CoordinatorId.Should().Be(newCoordinator.Id);
        result.AssignedSupervisorId.Should().Be(newSupervisor.Id);

        _notificationService.Verify(n => n.NotifyAsync(newCoordinator.Id, NotificationType.ContainerAssigned,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(n => n.NotifyAsync(newSupervisor.Id, NotificationType.ContainerAssigned,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), container.Id, It.IsAny<CancellationToken>()), Times.Once);
        _auditService.Verify(a => a.LogActivityAsync(container.Id, admin, "AssignmentsChanged",
            It.Is<string>(d => d.Contains("The coordinator is on leave."))), Times.Once);
    }

    [Fact]
    public async Task ReassignAsync_refuses_a_coordinator_from_another_department()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context));

        // A coordinator, but of somewhere else. Nothing that could reach this publication would
        // ever list them, so naming them here would strand it.
        var elsewhere = TestDataBuilder.Department(_fixture.Context, name: "Somewhere else", code: "SE");
        var outsider = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, outsider, RoleNames.Coordinator);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, outsider, elsewhere);

        var act = () => _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(outsider.Id, null, "Covering the vacancy."), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReassignAsync_moves_the_ethics_review_to_another_head_of_the_same_department()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            stage: PipelineStage.EthicsApproval);

        var firstHead = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, firstHead, department);
        var secondHead = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, secondHead, RoleNames.HeadOfDepartment);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, secondHead, department);

        _fixture.Context.EthicsApprovals.Add(new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingVerification,
            CoordinatorDecisionAt = DateTime.UtcNow,
            HeadOfDepartmentUserId = firstHead.Id
        });
        await _fixture.Context.SaveChangesAsync();

        await _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(null, null, "The first head is on leave.", secondHead.Id),
            Guid.NewGuid());

        (await _fixture.Reread().EthicsApprovals.FirstAsync(a => a.PublicationContainerId == container.Id))
            .HeadOfDepartmentUserId.Should().Be(secondHead.Id);
    }

    [Fact]
    public async Task ReassignAsync_refuses_a_head_of_another_department_for_the_ethics_review()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            stage: PipelineStage.EthicsApproval);

        var elsewhere = TestDataBuilder.Department(_fixture.Context, name: "Somewhere else", code: "SE");
        var outsider = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, outsider, RoleNames.HeadOfDepartment);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, outsider, elsewhere);

        _fixture.Context.EthicsApprovals.Add(new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingVerification,
            CoordinatorDecisionAt = DateTime.UtcNow
        });
        await _fixture.Context.SaveChangesAsync();

        var act = () => _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(null, null, "Trying somebody else.", outsider.Id), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    /// <summary>
    /// Ethics settled and no paper started yet. There is no publication row until the student
    /// opens the paper screen, and reading that absence as "nobody" dropped the publication to the
    /// bottom of every listing ordered by whose turn it is, on the screens their coordinator and
    /// head of department chase them from, at the one point somebody should have been chasing it.
    /// </summary>
    [Fact]
    public async Task A_publication_at_the_paper_stage_with_nothing_written_is_waiting_on_the_student()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            stage: PipelineStage.ResearchPaper);
        await _fixture.Context.SaveChangesAsync();

        var mine = await _sut.GetMineAsync(student.Id, new PageRequest());

        mine.Items.Should().ContainSingle(c => c.Id == container.Id)
            .Which.PaperAwaitingRole.Should().Be(RoleNames.Student);
    }

    /// <summary>
    /// And not before: a publication still choosing a topic or working through ethics is somebody
    /// else's turn, and the paper is not yet anybody's.
    /// </summary>
    [Theory]
    [InlineData(PipelineStage.ResearchProposals)]
    [InlineData(PipelineStage.EthicsApproval)]
    public async Task A_publication_short_of_the_paper_stage_is_waiting_on_nobody_for_a_paper(PipelineStage stage)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context), stage: stage);
        await _fixture.Context.SaveChangesAsync();

        var mine = await _sut.GetMineAsync(student.Id, new PageRequest());

        mine.Items.Should().ContainSingle(c => c.Id == container.Id)
            .Which.PaperAwaitingRole.Should().BeNull();
    }

    [Fact]
    public async Task MoveToAsync_puts_the_ethics_stage_back_to_the_student_and_clears_what_came_after()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            stage: PipelineStage.ResearchPaper);

        // As far on as it goes: everybody has decided.
        _fixture.Context.EthicsApprovals.Add(new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingVerification,
            SupervisorDecisionAt = DateTime.UtcNow,
            CoordinatorDecisionAt = DateTime.UtcNow,
            HeadOfDepartmentReviewedAt = DateTime.UtcNow,
            HeadOfDepartmentUserId = TestDataBuilder.User(_fixture.Context).Id
        });
        await _fixture.Context.SaveChangesAsync();

        await _sut.MoveToAsync(container.Id,
            new MoveContainerRequest((int)PipelineStage.EthicsApproval, "The consent form was the wrong one.",
                EthicsSteps.StudentUpload),
            Guid.NewGuid());

        var approval = await _fixture.Reread().EthicsApprovals.FirstAsync(a => a.PublicationContainerId == container.Id);
        approval.Status.Should().Be(EthicsStatus.PendingUpload);

        // Everything after the student's step is cleared, or it would land further on than asked.
        approval.CoordinatorDecisionAt.Should().BeNull();
        approval.HeadOfDepartmentReviewedAt.Should().BeNull();
        approval.HeadOfDepartmentUserId.Should().BeNull();
        approval.FinalDecisionAt.Should().BeNull();

        (await _fixture.Context.PublicationContainers.FindAsync(container.Id))!.CurrentPipeline
            .Should().Be(PipelineStage.EthicsApproval);
    }

    [Fact]
    public async Task MoveToAsync_restarts_the_ethics_clock_and_forgets_what_was_reported_about_the_old_step()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            stage: PipelineStage.EthicsApproval);

        // A review that has been sitting on somebody for two months, and has already been reported
        // late and warned about.
        var longAgo = DateTime.UtcNow.AddDays(-60);
        _fixture.Context.EthicsApprovals.Add(new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingSupervisorDecision,
            StepEnteredAt = longAgo,
            OverdueReportedAt = longAgo,
            DueSoonWarnedAt = longAgo
        });
        await _fixture.Context.SaveChangesAsync();

        await _sut.MoveToAsync(container.Id,
            new MoveContainerRequest((int)PipelineStage.EthicsApproval, "Starting this one again.",
                EthicsSteps.StudentUpload),
            Guid.NewGuid());

        var approval = await _fixture.Reread().EthicsApprovals.FirstAsync(a => a.PublicationContainerId == container.Id);

        // Whoever has it now gets the whole review period, and is warned about their own deadline
        // rather than passed over because the person before them had already been warned.
        approval.StepEnteredAt.Should().BeAfter(longAgo);
        approval.OverdueReportedAt.Should().BeNull();
        approval.DueSoonWarnedAt.Should().BeNull();
    }

    [Fact]
    public async Task MoveToAsync_lands_on_the_reading_asked_for_when_the_coordinator_reads_first()
    {
        _settingService.Setup(s => s.GetEthicsWorkflowSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EthicsWorkflowSettingsDto(true, true, true, true, SettingKeys.CoordinatorFirst));

        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context), stage: PipelineStage.EthicsApproval);

        var requirements = TestDataBuilder.EthicsDocumentRequirements(_fixture.Context);
        var approval = new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingVerification
        };
        _fixture.Context.EthicsApprovals.Add(approval);
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.EthicsDocuments.Add(new EthicsDocument
        {
            EthicsApprovalId = approval.Id,
            EthicsDocumentRequirementId = requirements[0].Id,
            FileName = "a.pdf",
            FilePath = "x/a.pdf",
            Version = 1,
            UploadedByUserId = student.Id,
            Status = EthicsDocumentStatus.Accepted
        });
        await _fixture.Context.SaveChangesAsync();

        await _sut.MoveToAsync(container.Id,
            new MoveContainerRequest((int)PipelineStage.EthicsApproval, "Back to the supervisor.",
                EthicsSteps.SupervisorDocumentReview),
            Guid.NewGuid());

        // The coordinator reads first here, so they have to count as done or the approval would
        // land on their step rather than the supervisor's.
        var moved = await _fixture.Reread().EthicsApprovals.FirstAsync(a => a.PublicationContainerId == container.Id);
        moved.CoordinatorDecisionAt.Should().NotBeNull();
        moved.SupervisorDocumentsReviewedAt.Should().BeNull();
    }

    [Fact]
    public async Task MoveToAsync_refuses_a_step_that_reads_documents_when_there_are_none()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            stage: PipelineStage.EthicsApproval);

        _fixture.Context.EthicsApprovals.Add(new EthicsApproval
        {
            PublicationContainerId = container.Id,
            Status = EthicsStatus.PendingUpload
        });
        await _fixture.Context.SaveChangesAsync();

        var act = () => _sut.MoveToAsync(container.Id,
            new MoveContainerRequest((int)PipelineStage.EthicsApproval, "Trying to skip ahead.",
                EthicsSteps.CoordinatorDocumentReview),
            Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task MoveToAsync_refuses_a_finished_publication()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            status: ContainerStatus.Completed);

        var act = () => _sut.MoveToAsync(container.Id,
            new MoveContainerRequest((int)PipelineStage.EthicsApproval, "Reopening."), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReassignAsync_throws_without_a_reason()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context));

        var newCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, newCoordinator, RoleNames.Coordinator);

        var act = () => _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(newCoordinator.Id, null, "   "), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReassignAsync_throws_once_the_publication_has_finished()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context),
            status: ContainerStatus.Completed);

        var newCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, newCoordinator, RoleNames.Coordinator);

        var act = () => _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(newCoordinator.Id, null, "Tidying up the record."), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReassignAsync_refuses_somebody_who_does_not_hold_the_role()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context));

        var somebody = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(somebody.Id, null, "Covering the vacancy."), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ReassignAsync_refuses_to_appoint_the_first_supervisor()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var container = TestDataBuilder.Container(
            _fixture.Context, student, TestDataBuilder.User(_fixture.Context));

        var supervisor = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.GrantRole(_fixture.Context, supervisor, RoleNames.Supervisor);

        var act = () => _sut.ReassignAsync(container.Id,
            new ReassignContainerRequest(null, supervisor.Id, "Nobody is supervising this."), Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    /// <summary>
    /// Three dashboards offer a Stage column. An unknown sort name falls back to the default
    /// order, so the arrow moved, the rows did not, and the screen answered without doing
    /// anything. Both directions are asserted: a name nothing accepts returns the same order
    /// whichever way it is asked for, which is exactly what this has to rule out.
    /// </summary>
    [Theory]
    [InlineData(false, PipelineStage.ResearchProposals)]
    [InlineData(true, PipelineStage.ResearchPaper)]
    public async Task GetAllAsync_orders_by_stage(bool descending, PipelineStage expectedFirst)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var coordinator = TestDataBuilder.User(_fixture.Context);

        foreach (var stage in new[]
                 {
                     PipelineStage.EthicsApproval, PipelineStage.ResearchPaper, PipelineStage.ResearchProposals
                 })
        {
            var student = TestDataBuilder.User(_fixture.Context);
            TestDataBuilder.StudentProfile(_fixture.Context, student, department);
            TestDataBuilder.Container(_fixture.Context, student, coordinator, stage: stage);
        }

        var result = await _sut.GetAllAsync(new ContainerQuery
        {
            SortBy = "stage",
            SortDescending = descending
        });

        result.Items.Should().HaveCount(3);
        result.Items[0].CurrentPipeline.Should().Be((int)expectedFirst);
    }
}
