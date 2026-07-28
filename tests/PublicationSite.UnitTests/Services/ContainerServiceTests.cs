using FluentAssertions;
using Moq;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class ContainerServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<IDepartmentService> _departmentService = new();
    private readonly Mock<IContainerAccessService> _accessService = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly ContainerService _sut;

    public ContainerServiceTests()
    {
        _accessService.Setup(a => a.EnsureAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns(Task.CompletedTask);
        _sut = new ContainerService(_fixture.Context, _departmentService.Object, _accessService.Object, _auditService.Object);
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
    public async Task CreateAsync_throws_when_student_already_has_a_container()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.Container(_fixture.Context, student, coordinator);

        var act = () => _sut.CreateAsync(student.Id);

        await act.Should().ThrowAsync<ConflictException>();
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
