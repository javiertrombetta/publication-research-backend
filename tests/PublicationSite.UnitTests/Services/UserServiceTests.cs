using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class UserServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager = IdentityMockFactory.MockUserManager();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _emailSender.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _userManager.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("reset-token");
        _userManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<string>());

        _sut = new UserService(_userManager.Object, _fixture.Context, _emailSender.Object, _auditService.Object, Options.Create(new FrontendSettings()));
    }

    public void Dispose() => _fixture.Dispose();

    private void SetupCreateAsyncPersistsUser()
    {
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>()))
            .Returns<ApplicationUser>(async user =>
            {
                user.Id = Guid.NewGuid();
                _fixture.Context.Users.Add(user);
                await _fixture.Context.SaveChangesAsync();
                return IdentityResult.Success;
            });
    }

    [Fact]
    public async Task CreateAsync_rejects_unrecognised_role()
    {
        var request = new CreateUserRequest { Email = "x@ais.ac.nz", FirstName = "A", LastName = "B", Role = "Wizard" };

        var act = () => _sut.CreateAsync(request, Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateAsync_requires_department_for_supervisor_role()
    {
        SetupCreateAsyncPersistsUser();
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Supervisor)).ReturnsAsync(IdentityResult.Success);

        var request = new CreateUserRequest { Email = "sup@ais.ac.nz", FirstName = "A", LastName = "B", Role = RoleNames.Supervisor };

        var act = () => _sut.CreateAsync(request, Guid.NewGuid());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateAsync_creates_enabled_user_with_profile_and_sends_set_password_email()
    {
        SetupCreateAsyncPersistsUser();
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Coordinator)).ReturnsAsync(IdentityResult.Success);
        var department = TestDataBuilder.Department(_fixture.Context);

        var request = new CreateUserRequest
        {
            Email = "coord@ais.ac.nz", FirstName = "Coordinator", LastName = "One", Role = RoleNames.Coordinator, DepartmentId = department.Id
        };

        await _sut.CreateAsync(request, Guid.NewGuid());

        _fixture.Context.CoordinatorProfiles.Should().ContainSingle(c => c.DepartmentId == department.Id);
        _emailSender.Verify(e => e.SendAsync("coord@ais.ac.nz", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_rejects_second_head_of_department_for_same_department()
    {
        SetupCreateAsyncPersistsUser();
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.HeadOfDepartment)).ReturnsAsync(IdentityResult.Success);

        var department = TestDataBuilder.Department(_fixture.Context);
        var existingHod = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.HeadOfDepartmentProfile(_fixture.Context, existingHod, department);

        var request = new CreateUserRequest
        {
            Email = "hod2@ais.ac.nz", FirstName = "A", LastName = "B", Role = RoleNames.HeadOfDepartment, DepartmentId = department.Id
        };

        var act = () => _sut.CreateAsync(request, Guid.NewGuid());

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ChangeRoleAsync_replaces_existing_roles()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) =>
            _fixture.Context.Users.Find(Guid.Parse(id)));
        _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync([RoleNames.Staff]);
        _userManager.Setup(m => m.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.AddToRoleAsync(user, RoleNames.Supervisor)).ReturnsAsync(IdentityResult.Success);

        await _sut.ChangeRoleAsync(user.Id, new ChangeUserRoleRequest(RoleNames.Supervisor, "Promoted"), Guid.NewGuid());

        _userManager.Verify(m => m.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains(RoleNames.Staff))), Times.Once);
        _userManager.Verify(m => m.AddToRoleAsync(user, RoleNames.Supervisor), Times.Once);
    }

    [Fact]
    public async Task EnableAsync_and_DisableAsync_toggle_status()
    {
        var user = TestDataBuilder.User(_fixture.Context, status: Api.Enums.UserStatus.Disabled);
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) =>
            _fixture.Context.Users.Find(Guid.Parse(id)));
        _userManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        await _sut.EnableAsync(user.Id, "Verified manually", Guid.NewGuid());
        (await _sut.GetByIdAsync(user.Id)).Status.Should().Be(Api.Enums.UserStatus.Enabled.ToString());

        await _sut.DisableAsync(user.Id, "Policy violation", Guid.NewGuid());
        (await _sut.GetByIdAsync(user.Id)).Status.Should().Be(Api.Enums.UserStatus.Disabled.ToString());
    }

    [Fact]
    public async Task ResetPasswordAsync_sends_email_with_reset_link()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) =>
            _fixture.Context.Users.Find(Guid.Parse(id)));

        await _sut.ResetPasswordAsync(user.Id, "Forgot password", Guid.NewGuid());

        _emailSender.Verify(e => e.SendAsync(user.Email!, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateOwnProfileAsync_updates_student_profile_fields()
    {
        var department = TestDataBuilder.Department(_fixture.Context);
        var user = TestDataBuilder.User(_fixture.Context);
        TestDataBuilder.StudentProfile(_fixture.Context, user, department);
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) =>
            _fixture.Context.Users.Find(Guid.Parse(id)));
        _userManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var request = new UpdateMyProfileRequest("NewFirst", "NewLast", "PhD", "2027", null, "0000-0000", null, null, null);
        var result = await _sut.UpdateOwnProfileAsync(user.Id, request);

        result.FirstName.Should().Be("NewFirst");
        _fixture.Context.StudentProfiles.Single(s => s.UserId == user.Id).Programme.Should().Be("PhD");
    }
}
