using FluentAssertions;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Containers;
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
    private readonly Mock<ISystemSettingService> _settingService = new();
    private readonly ContainerService _sut;

    public ContainerServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);

        // Creating a container snapshots the committee rules onto it, so they have to answer.
        _settingService.Setup(s => s.GetCommitteeSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitteeSettingsDto(2, 1, 2, RoleNames.CommitteeEligible, [], RoleNames.CommitteeEligible));

        _sut = new ContainerService(_fixture.Context, _departmentService.Object, _accessService.Object,
            _auditService.Object, _settingService.Object);
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
        var student = TestDataBuilder.User(_fixture.Context);
        var coordinator = TestDataBuilder.User(_fixture.Context);

        var result = await _sut.AssignCoordinatorManuallyAsync(
            new AssignCoordinatorRequest(student.Id, coordinator.Id, "Manual assignment"), Guid.NewGuid());

        result.StudentId.Should().Be(student.Id);
        result.CoordinatorId.Should().Be(coordinator.Id);
    }

    [Fact]
    public async Task AssignCoordinatorManuallyAsync_reassigns_coordinator_on_existing_container()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var originalCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.Container(_fixture.Context, student, originalCoordinator);

        var newCoordinator = TestDataBuilder.User(_fixture.Context);

        var result = await _sut.AssignCoordinatorManuallyAsync(
            new AssignCoordinatorRequest(student.Id, newCoordinator.Id, "Reassigning"), Guid.NewGuid());

        result.CoordinatorId.Should().Be(newCoordinator.Id);
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
}
