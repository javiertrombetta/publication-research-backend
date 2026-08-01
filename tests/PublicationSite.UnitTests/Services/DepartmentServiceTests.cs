using FluentAssertions;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Departments;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class DepartmentServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly DepartmentService _sut;

    public DepartmentServiceTests()
    {
        _sut = new DepartmentService(_fixture.Context);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CreateAsync_persists_department()
    {
        var result = await _sut.CreateAsync(new CreateDepartmentRequest("Computer Science", "CS"));

        result.Name.Should().Be("Computer Science");
        (await _sut.GetByIdAsync(result.Id)).Code.Should().Be("CS");
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_code()
    {
        await _sut.CreateAsync(new CreateDepartmentRequest("Computer Science", "CS"));

        var act = () => _sut.CreateAsync(new CreateDepartmentRequest("Computer Studies", "CS"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task DeleteAsync_rejects_department_still_in_use()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);

        var act = () => _sut.DeleteAsync(department.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SelectCoordinatorForDepartmentAsync_picks_coordinator_with_fewest_active_containers()
    {
        var department = TestDataBuilder.Department(_fixture.Context);

        var busyCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, busyCoordinator, department);
        TestDataBuilder.GrantRole(_fixture.Context, busyCoordinator, RoleNames.Coordinator);

        var freeCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, freeCoordinator, department);
        TestDataBuilder.GrantRole(_fixture.Context, freeCoordinator, RoleNames.Coordinator);

        // Give the "busy" coordinator two active containers, the "free" one none.
        var student1 = TestDataBuilder.User(_fixture.Context);
        var student2 = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.Container(_fixture.Context, student1, busyCoordinator);
        TestDataBuilder.Container(_fixture.Context, student2, busyCoordinator);

        var selected = await _sut.SelectCoordinatorForDepartmentAsync(department.Id);

        selected.Should().Be(freeCoordinator.Id);
    }

    [Fact]
    public async Task SelectCoordinatorForDepartmentAsync_ignores_unavailable_coordinators()
    {
        var department = TestDataBuilder.Department(_fixture.Context);

        var onLeave = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, onLeave, department, isAvailable: false);
        TestDataBuilder.GrantRole(_fixture.Context, onLeave, RoleNames.Coordinator);

        var available = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, available, department, isAvailable: true);
        TestDataBuilder.GrantRole(_fixture.Context, available, RoleNames.Coordinator);

        var selected = await _sut.SelectCoordinatorForDepartmentAsync(department.Id);

        selected.Should().Be(available.Id);
    }

    [Fact]
    public async Task SelectCoordinatorForDepartmentAsync_ignores_disabled_coordinators()
    {
        var department = TestDataBuilder.Department(_fixture.Context);

        var disabled = TestDataBuilder.User(_fixture.Context, status: UserStatus.Disabled);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, disabled, department);
        TestDataBuilder.GrantRole(_fixture.Context, disabled, RoleNames.Coordinator);

        var enabled = TestDataBuilder.User(_fixture.Context, status: UserStatus.Enabled);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, enabled, department);
        TestDataBuilder.GrantRole(_fixture.Context, enabled, RoleNames.Coordinator);

        var selected = await _sut.SelectCoordinatorForDepartmentAsync(department.Id);

        selected.Should().Be(enabled.Id);
    }

    /// <summary>
    /// A profile outlives the role that created it. They are never deleted, because Publication
    /// Containers point at them, so someone moved off Coordinator keeps a Coordinator Profile. They
    /// must stop being handed new students the moment the role goes.
    /// </summary>
    [Fact]
    public async Task SelectCoordinatorForDepartmentAsync_ignores_a_profile_whose_owner_no_longer_holds_the_role()
    {
        var department = TestDataBuilder.Department(_fixture.Context);

        // Has the profile but was never granted the role: the state left behind by a demotion.
        var formerCoordinator = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CoordinatorProfile(_fixture.Context, formerCoordinator, department);

        var act = () => _sut.SelectCoordinatorForDepartmentAsync(department.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SelectCoordinatorForDepartmentAsync_throws_when_no_coordinator_available()
    {
        var department = TestDataBuilder.Department(_fixture.Context);

        var act = () => _sut.SelectCoordinatorForDepartmentAsync(department.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
