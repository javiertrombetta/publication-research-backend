using FluentAssertions;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class ContainerAccessServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly ContainerAccessService _sut;

    public ContainerAccessServiceTests()
    {
        _sut = new ContainerAccessService(_fixture.Context);
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Grants a role by writing the rows Identity would, rather than by telling a mock what to
    /// answer. The service reads roles from the database now — one query instead of a call per
    /// question — so a mocked UserManager would no longer be testing anything it uses.
    /// </summary>
    private void SetupUser(ApplicationUser user, bool isAdmin = false, bool isHeadOfDepartment = false)
    {
        if (isAdmin) GrantRole(user, RoleNames.Admin);
        if (isHeadOfDepartment) GrantRole(user, RoleNames.HeadOfDepartment);
    }

    private void GrantRole(ApplicationUser user, string roleName)
    {
        var role = _fixture.Context.Roles.FirstOrDefault(r => r.Name == roleName);
        if (role is null)
        {
            role = new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() };
            _fixture.Context.Roles.Add(role);
            _fixture.Context.SaveChanges();
        }

        _fixture.Context.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = role.Id
        });
        _fixture.Context.SaveChanges();
    }

    [Fact]
    public async Task Unknown_user_is_denied()
    {
        var container = SeedContainer();

        var result = await _sut.CanAccessAsync(container.Id, Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_can_access_any_container()
    {
        var container = SeedContainer();
        var admin = TestDataBuilder.User(_fixture.Context);
        SetupUser(admin, isAdmin: true);

        var result = await _sut.CanAccessAsync(container.Id, admin.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Owning_student_can_access_their_container()
    {
        var container = SeedContainer(out var student, out _);
        SetupUser(student);

        var result = await _sut.CanAccessAsync(container.Id, student.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Unrelated_student_is_denied()
    {
        var container = SeedContainer();
        var stranger = TestDataBuilder.User(_fixture.Context);
        SetupUser(stranger);

        var result = await _sut.CanAccessAsync(container.Id, stranger.Id);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Assigned_coordinator_can_access()
    {
        var container = SeedContainer(out _, out var coordinator);
        SetupUser(coordinator);

        var result = await _sut.CanAccessAsync(container.Id, coordinator.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Assigned_supervisor_can_access()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var supervisor = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator, supervisor);
        SetupUser(supervisor);

        var result = await _sut.CanAccessAsync(container.Id, supervisor.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Head_of_department_can_access_containers_from_their_department()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        var hod = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, hod, department);
        SetupUser(hod, isHeadOfDepartment: true);

        var result = await _sut.CanAccessAsync(container.Id, hod.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Head_of_department_from_a_different_department_is_denied()
    {
        var studentDepartment = TestDataBuilder.Department(_fixture.Context);
        var otherDepartment = TestDataBuilder.Department(_fixture.Context);

        var student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, studentDepartment);
        var coordinator = TestDataBuilder.User(_fixture.Context);
        var container = TestDataBuilder.Container(_fixture.Context, student, coordinator);

        var hod = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, hod, otherDepartment);
        SetupUser(hod, isHeadOfDepartment: true);

        var result = await _sut.CanAccessAsync(container.Id, hod.Id);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Committee_member_assigned_to_the_publication_can_access()
    {
        var container = SeedContainer();
        var publication = new Publication { PublicationContainerId = container.Id, Title = "T", Abstract = "A" };
        _fixture.Context.Publications.Add(publication);
        _fixture.Context.SaveChanges();

        var member = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.CommitteeMemberProfile(_fixture.Context, member);

        var creator = TestDataBuilder.User(_fixture.Context);
        var committee = new Committee { PublicationId = publication.Id, CreatedByUserId = creator.Id, MinApprovalsRequired = 1 };
        committee.Members.Add(new CommitteeMember { UserId = member.Id, RoleType = Api.Enums.CommitteeMemberRoleType.Internal });
        _fixture.Context.Committees.Add(committee);
        _fixture.Context.SaveChanges();

        SetupUser(member);

        var result = await _sut.CanAccessAsync(container.Id, member.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureAccessAsync_throws_forbidden_when_denied()
    {
        var container = SeedContainer();
        var stranger = TestDataBuilder.User(_fixture.Context);
        SetupUser(stranger);

        var act = () => _sut.EnsureAccessAsync(container.Id, stranger.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    private PublicationContainer SeedContainer() => SeedContainer(out _, out _);

    private PublicationContainer SeedContainer(out ApplicationUser student, out ApplicationUser coordinator)
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        student = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, student, department);
        coordinator = TestDataBuilder.User(_fixture.Context);
        return TestDataBuilder.Container(_fixture.Context, student, coordinator);
    }
}
