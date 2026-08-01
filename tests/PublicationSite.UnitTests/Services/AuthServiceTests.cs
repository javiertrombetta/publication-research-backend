using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class AuthServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager = IdentityMockFactory.MockUserManager();
    private readonly Mock<SignInManager<ApplicationUser>> _signInManager;
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IAuditService> _auditService = new();
    private readonly Mock<ISystemSettingService> _settingService = new();
    private readonly Mock<IAccountLockoutService> _lockoutService = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _signInManager = IdentityMockFactory.MockSignInManager(_userManager.Object);
        _emailSender.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userManager.Setup(m => m.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("token");

        // The defaults: passwords never expire and nothing is locked out, so a test only has to
        // say otherwise when that is what it is about.
        _settingService.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordSettingsDto(10, true, true, true, true, 0, 5, 15));

        // Registration now asks whether anyone may sign themselves up, and which domains mean
        // what. Open with the usual domains, so these tests stay about registration itself.
        _settingService.Setup(s => s.GetAccessSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessSettingsDto("Open", false, false, false, 14, 30, 14));

        _settingService.Setup(s => s.GetInstitutionSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstitutionSettingsDto(
                "Auckland Institute of Studies", "@aisstudent.ac.nz", "@ais.ac.nz", null, null, null, null));

        _sut = new AuthService(
            _userManager.Object, _fixture.Context, _tokenService.Object,
            _emailSender.Object, _auditService.Object, _settingService.Object, _lockoutService.Object,
            Options.Create(new FrontendSettings()));
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>Mimics what the real EF-backed UserStore does: persists the user being registered.</summary>
    private void SetupCreateAsyncPersistsUser()
    {
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Returns<ApplicationUser, string>(async (user, _) =>
            {
                user.Id = Guid.NewGuid();
                _fixture.Context.Users.Add(user);
                await _fixture.Context.SaveChangesAsync();
                return IdentityResult.Success;
            });
    }

    [Fact]
    public async Task RegisterAsync_rejects_non_institutional_email_domain()
    {
        var request = new RegisterRequest { Email = "person@gmail.com", Password = "SuperSecret123!", FirstName = "A", LastName = "B" };

        var act = () => _sut.RegisterAsync(request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RegisterAsync_assigns_student_role_and_creates_profile_for_student_domain()
    {
        SetupCreateAsyncPersistsUser();
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Student)).ReturnsAsync(IdentityResult.Success);
        var department = TestDataBuilder.Department(_fixture.Context);

        var request = new RegisterRequest
        {
            Email = "student@aisstudent.ac.nz", Password = "SuperSecret123!", FirstName = "A", LastName = "B",
            DepartmentId = department.Id, Cohort = "2026", StudentIdNumber = "S1", Programme = "MSc"
        };

        await _sut.RegisterAsync(request);

        _userManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Student), Times.Once);
        _fixture.Context.StudentProfiles.Should().ContainSingle(s => s.Cohort == "2026" && s.DepartmentId == department.Id);
    }

    [Fact]
    public async Task RegisterAsync_assigns_staff_role_without_a_profile_for_staff_domain()
    {
        SetupCreateAsyncPersistsUser();
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Staff)).ReturnsAsync(IdentityResult.Success);

        var request = new RegisterRequest { Email = "staff@ais.ac.nz", Password = "SuperSecret123!", FirstName = "A", LastName = "B" };

        await _sut.RegisterAsync(request);

        _userManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Staff), Times.Once);
        _fixture.Context.StudentProfiles.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterAsync_requires_student_fields_for_student_domain()
    {
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleNames.Student)).ReturnsAsync(IdentityResult.Success);

        var request = new RegisterRequest { Email = "student@aisstudent.ac.nz", Password = "SuperSecret123!", FirstName = "A", LastName = "B" };

        var act = () => _sut.RegisterAsync(request);

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact]
    public async Task RegisterAsync_surfaces_identity_creation_errors()
    {
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));

        var request = new RegisterRequest { Email = "staff@ais.ac.nz", Password = "weak", FirstName = "A", LastName = "B" };

        var act = () => _sut.RegisterAsync(request);

        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact]
    public async Task LoginAsync_rejects_unknown_email()
    {
        _userManager.Setup(m => m.FindByEmailAsync("missing@ais.ac.nz")).ReturnsAsync((ApplicationUser?)null);

        var act = () => _sut.LoginAsync(new LoginRequest("missing@ais.ac.nz", "whatever"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task LoginAsync_rejects_wrong_password()
    {
        var user = new ApplicationUser { Email = "user@ais.ac.nz", Status = UserStatus.Enabled };
        _userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);

        var act = () => _sut.LoginAsync(new LoginRequest(user.Email, "wrong"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    /// <summary>
    /// Lockout is checked before the password, so a locked account is never told whether the
    /// password it was given happens to be right. Asserted by leaving CheckPasswordAsync
    /// unconfigured. It would return false, and still expecting the lockout's own message.
    /// </summary>
    [Fact]
    public async Task LoginAsync_rejects_locked_out_account_before_checking_the_password()
    {
        var user = new ApplicationUser { Email = "user@ais.ac.nz", Status = UserStatus.Enabled };
        _userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _lockoutService.Setup(l => l.EnsureNotLockedOutAsync(user, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Too many failed attempts. This account is locked for another 15 minutes."));

        var act = () => _sut.LoginAsync(new LoginRequest(user.Email, "pw"));

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("locked");
    }

    [Fact]
    public async Task LoginAsync_records_a_wrong_password_against_the_lockout()
    {
        var user = new ApplicationUser { Email = "user@ais.ac.nz", Status = UserStatus.Enabled };
        _userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "wrong")).ReturnsAsync(false);

        var act = () => _sut.LoginAsync(new LoginRequest(user.Email, "wrong"));

        await act.Should().ThrowAsync<ForbiddenException>();
        _lockoutService.Verify(l => l.RecordFailureAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Zero expiry days means passwords never expire, so an old one still signs in. The opposite
    /// case is covered by the settings service's own validation.
    /// </summary>
    [Fact]
    public async Task LoginAsync_rejects_a_password_older_than_the_configured_expiry()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), Email = "user@ais.ac.nz", FirstName = "A", LastName = "B",
            Status = UserStatus.Enabled, PasswordChangedAt = DateTime.UtcNow.AddDays(-120)
        };
        _userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "pw")).ReturnsAsync(true);
        _userManager.Setup(m => m.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset-token");
        _settingService.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordSettingsDto(10, true, true, true, true, 90, 5, 15));

        var act = () => _sut.LoginAsync(new LoginRequest(user.Email, "pw"));

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain("expired");
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Disabled)]
    public async Task LoginAsync_rejects_accounts_that_cannot_log_in(UserStatus status)
    {
        var user = new ApplicationUser { Email = "user@ais.ac.nz", Status = status };
        _userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "pw")).ReturnsAsync(true);

        var act = () => _sut.LoginAsync(new LoginRequest(user.Email, "pw"));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task LoginAsync_succeeds_and_issues_tokens_for_enabled_user()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@ais.ac.nz", FirstName = "A", LastName = "B", Status = UserStatus.Enabled };
        _userManager.Setup(m => m.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, "pw")).ReturnsAsync(true);
        _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync([RoleNames.Coordinator]);

        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        _tokenService.Setup(t => t.IssueTokensAsync(user, It.IsAny<IList<string>>()))
            .ReturnsAsync(new TokenPair("access-token", "refresh-token", expiresAt));

        var result = await _sut.LoginAsync(new LoginRequest(user.Email, "pw"));

        result.AccessToken.Should().Be("access-token");
        result.User.Roles.Should().Contain(RoleNames.Coordinator);
    }

    [Fact]
    public async Task VerifyEmailAsync_throws_when_user_missing()
    {
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var act = () => _sut.VerifyEmailAsync(Guid.NewGuid(), "token");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task VerifyEmailAsync_enables_the_account_on_success()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@ais.ac.nz", Status = UserStatus.Pending };
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.ConfirmEmailAsync(user, "token")).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        await _sut.VerifyEmailAsync(user.Id, "token");

        user.Status.Should().Be(UserStatus.Enabled);
    }

    [Fact]
    public async Task ForgotPasswordAsync_does_not_throw_for_unknown_email()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var act = () => _sut.ForgotPasswordAsync("unknown@ais.ac.nz");

        await act.Should().NotThrowAsync();
        _emailSender.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePasswordAsync_surfaces_identity_errors()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@ais.ac.nz" };
        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _userManager.Setup(m => m.ChangePasswordAsync(user, "old", "new"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Incorrect password" }));

        var act = () => _sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest("old", "new"));

        await act.Should().ThrowAsync<ValidationAppException>();
    }
}
