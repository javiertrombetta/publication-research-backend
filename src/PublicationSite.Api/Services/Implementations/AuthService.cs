using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    ITokenService tokenService,
    IEmailSender emailSender,
    IAuditService auditService,
    ISystemSettingService settingService,
    IAccountLockoutService lockoutService,
    IOptions<FrontendSettings> frontendOptions) : IAuthService
{
    private readonly FrontendSettings _frontend = frontendOptions.Value;

    public async Task<bool> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Whether anyone may sign themselves up at all. Checked before the address is even
        // looked at: on a closed system the answer is the same for every address, and saying so
        // plainly is better than rejecting each one for a different-sounding reason.
        var access = await settingService.GetAccessSettingsAsync(cancellationToken);
        if (access.RegistrationMode != SettingKeys.RegistrationModeOpen)
        {
            throw new ForbiddenException(
                "Accounts are created by invitation. Ask an administrator to invite you.");
        }

        var role = await ResolveRoleFromEmailAsync(request.Email, cancellationToken);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            InstitutionalId = request.InstitutionalId,
            Status = UserStatus.Pending,
            AuthProvider = AuthProvider.Local
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            throw new ValidationAppException(createResult.Errors.Select(e => e.Description).ToList());
        }

        await StampPasswordChangedAsync(user);
        await userManager.AddToRoleAsync(user, role);

        if (role == RoleNames.Student)
        {
            await CreateStudentProfileAsync(user, request, cancellationToken);
        }

        return await SendEmailVerificationAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email)
            ?? throw new ForbiddenException("Invalid email or password.");

        // Checked before the password, so a locked account never learns whether the password
        // it was given happens to be the right one.
        await lockoutService.EnsureNotLockedOutAsync(user, cancellationToken);

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            if (await lockoutService.RecordFailureAsync(user, cancellationToken))
            {
                // Say so plainly on the attempt that locks it, rather than letting the next
                // attempt be the first the person hears of it.
                await lockoutService.EnsureNotLockedOutAsync(user, cancellationToken);
            }

            throw new ForbiddenException("Invalid email or password.");
        }

        await lockoutService.RecordSuccessAsync(user, cancellationToken);
        EnsureUserCanLogIn(user);
        await EnsurePasswordHasNotExpiredAsync(user, cancellationToken);

        var response = await BuildAuthResponseAsync(user);
        await auditService.LogAuditAsync(user.Id, "UserLoggedIn", nameof(ApplicationUser), user.Id);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // Asked about the account before the token is exchanged, and the exchange refused if the
        // answer is no. Refresh used to ask nothing at all, so disabling somebody stopped them
        // signing in again and did nothing whatever to the session they already had: it renewed
        // itself every hour for as long as their browser stayed open. Deleting an account left the
        // same door open, since deletion disables rather than removes.
        // The status and nothing else. A lockout is five wrong passwords, which anybody who knows
        // an address can produce, so ending live sessions on it would hand out a way to throw
        // people off the site. Sign-in is already refused while it lasts, which is what it is for.
        var user = await GetUserByRefreshTokenAsync(refreshToken);
        EnsureUserCanLogIn(user);

        var pair = await tokenService.RefreshAsync(refreshToken);
        return await BuildAuthResponseAsync(user, pair);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        tokenService.RevokeAsync(refreshToken);

    public async Task VerifyEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var decodedToken = HttpUtility.UrlDecode(token);
        var result = await userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            throw new ValidationAppException(result.Errors.Select(e => e.Description).ToList());
        }

        user.Status = UserStatus.Enabled;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await auditService.LogAuditAsync(user.Id, "EmailVerified", nameof(ApplicationUser), user.Id);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Do not reveal whether the address is registered.
            return;
        }

        await SendPasswordResetEmailAsync(user);
    }

    /// <summary>
    /// Returns whether the message went out. Forgot-password ignores that on purpose. Saying an
    /// email failed would reveal that the address is registered, which is the one thing that
    /// endpoint is careful not to disclose. The expiry path does report it, because the person
    /// asking is already known to exist and is otherwise left stranded.
    /// </summary>
    private async Task<bool> SendPasswordResetEmailAsync(ApplicationUser user)
    {
        var email = user.Email!;
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

        return await emailSender.SendAsync(email, "Reset your password",
            $"<p>Click the link below to reset your password:</p><p><a href=\"{link}\">Reset password</a></p>");
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email)
            ?? throw new NotFoundException(nameof(ApplicationUser), request.Email);

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new ValidationAppException(result.Errors.Select(e => e.Description).ToList());
        }

        await StampPasswordChangedAsync(user);
        await auditService.LogAuditAsync(user.Id, "PasswordReset", nameof(ApplicationUser), user.Id);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        await lockoutService.EnsureNotLockedOutAsync(user, cancellationToken);

        // The current password is checked separately from the change so that getting it wrong
        // counts towards a lockout. Left to ChangePasswordAsync, this form would be an
        // unlimited guessing oracle for anyone who found a signed-in session unattended.
        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            if (await lockoutService.RecordFailureAsync(user, cancellationToken))
            {
                await lockoutService.EnsureNotLockedOutAsync(user, cancellationToken);
            }

            throw new ValidationAppException(["That is not your current password."]);
        }

        await lockoutService.RecordSuccessAsync(user, cancellationToken);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new ValidationAppException(result.Errors.Select(e => e.Description).ToList());
        }

        await StampPasswordChangedAsync(user);
        await auditService.LogAuditAsync(user.Id, "PasswordChanged", nameof(ApplicationUser), user.Id);
    }

    public async Task<AuthResponse> LoginWithAzureSsoAsync(ClaimsPrincipal azureAdPrincipal, CancellationToken cancellationToken = default)
    {
        var email = azureAdPrincipal.FindFirstValue(ClaimTypes.Upn)
            ?? azureAdPrincipal.FindFirstValue(ClaimTypes.Email)
            ?? azureAdPrincipal.FindFirstValue("preferred_username")
            ?? throw new ForbiddenException("The Azure AD token does not contain an email claim.");

        var azureObjectId = azureAdPrincipal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? azureAdPrincipal.FindFirstValue("oid");

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            var role = await ResolveRoleFromEmailAsync(email, cancellationToken);
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = azureAdPrincipal.FindFirstValue(ClaimTypes.GivenName) ?? string.Empty,
                LastName = azureAdPrincipal.FindFirstValue(ClaimTypes.Surname) ?? string.Empty,
                Status = UserStatus.Pending,
                AuthProvider = AuthProvider.AzureSso,
                AzureObjectId = azureObjectId
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                throw new ValidationAppException(createResult.Errors.Select(e => e.Description).ToList());
            }

            await userManager.AddToRoleAsync(user, role);
            var verificationSent = await SendEmailVerificationAsync(user);

            throw new ForbiddenException(verificationSent
                ? "Account created. Please check your email to verify your address before logging in."
                : "Account created, but the verification email could not be sent. " +
                  "Ask an administrator to check the mail server.");
        }

        EnsureUserCanLogIn(user);

        var response = await BuildAuthResponseAsync(user);
        await auditService.LogAuditAsync(user.Id, "UserLoggedInViaAzureSso", nameof(ApplicationUser), user.Id);
        return response;
    }

    /// <summary>
    /// Records when the password was set, so expiry has something to count from.
    /// </summary>
    private async Task StampPasswordChangedAsync(ApplicationUser user)
    {
        user.PasswordChangedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
    }

    /// <summary>
    /// Refuses a sign-in whose password is older than the configured lifetime, and sends a reset
    /// link so the person is not simply stuck. Zero days means no expiry, which is the default.
    ///
    /// A password with no recorded change date is treated as current: the column was added after
    /// these accounts existed, and reading null as "infinitely old" would lock out everyone who
    /// had not signed in since, the moment an administrator first switched expiry on.
    /// </summary>
    private async Task EnsurePasswordHasNotExpiredAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var policy = await settingService.GetPasswordSettingsAsync(cancellationToken);
        if (policy.ExpiryDays <= 0 || user.PasswordChangedAt is not { } changedAt)
        {
            return;
        }

        var age = DateTime.UtcNow - changedAt;
        if (age.TotalDays < policy.ExpiryDays)
        {
            return;
        }

        var sent = await SendPasswordResetEmailAsync(user);

        throw new ForbiddenException(sent
            ? $"Your password expired after {policy.ExpiryDays} days. We have emailed you a link to set a new one."
            : $"Your password expired after {policy.ExpiryDays} days, and the reset email could not be sent. " +
              "Ask an administrator to reset it for you.");
    }

    private static void EnsureUserCanLogIn(ApplicationUser user)
    {
        switch (user.Status)
        {
            case UserStatus.Pending:
                throw new ForbiddenException("Please verify your email address before logging in.");
            case UserStatus.Disabled:
                throw new ForbiddenException("Your account has been disabled. Contact an administrator.");
        }
    }

    private async Task<bool> SendEmailVerificationAsync(ApplicationUser user)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        // Reported, not thrown. The account is already created, so failing here would return an
        // error for something that succeeded, and the retry would then collide with the address it
        // had just taken.
        return await emailSender.SendAsync(user.Email!, "Verify your email address",
            $"<p>Welcome to the AIS Research Publication Site. Please verify your email address:</p><p><a href=\"{link}\">Verify email</a></p>");
    }

    private async Task CreateStudentProfileAsync(ApplicationUser user, RegisterRequest request, CancellationToken cancellationToken)
    {
        if (request.DepartmentId is null || string.IsNullOrWhiteSpace(request.Cohort) ||
            string.IsNullOrWhiteSpace(request.StudentIdNumber) || string.IsNullOrWhiteSpace(request.Programme))
        {
            throw new ValidationAppException(
                ["Department, Cohort, Student ID and Programme are required for student registration."]);
        }

        var department = await db.Departments.FindAsync([request.DepartmentId.Value], cancellationToken)
            ?? throw new NotFoundException(nameof(Department), request.DepartmentId.Value);

        var researchAreas = request.ResearchAreaIds is { Count: > 0 }
            ? await db.ResearchAreas.Where(r => request.ResearchAreaIds.Contains(r.Id)).ToListAsync(cancellationToken)
            : [];

        db.StudentProfiles.Add(new StudentProfile
        {
            UserId = user.Id,
            DepartmentId = department.Id,
            Cohort = request.Cohort,
            StudentIdNumber = request.StudentIdNumber,
            Programme = request.Programme,
            ResearchAreas = researchAreas
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// What someone is, judged by their address. The two domains were constants; an institution
    /// that takes on a second student domain should not need a deployment to accept it.
    ///
    /// Staff is deliberately a holding role rather than an operational one: it lets the person
    /// sign in and see their profile, and an administrator grants them what they actually do.
    /// </summary>
    private async Task<string> ResolveRoleFromEmailAsync(string email, CancellationToken cancellationToken)
    {
        var institution = await settingService.GetInstitutionSettingsAsync(cancellationToken);

        if (email.EndsWith(institution.StudentEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return RoleNames.Student;
        }

        if (email.EndsWith(institution.StaffEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return RoleNames.Staff;
        }

        throw new BusinessRuleException(
            $"Only '{institution.StudentEmailDomain}' or '{institution.StaffEmailDomain}' addresses can register here. " +
            "Anyone outside the institution has to be invited.");
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user, Services.Interfaces.TokenPair? pair = null)
    {
        var roles = await userManager.GetRolesAsync(user);
        pair ??= await tokenService.IssueTokensAsync(user, roles);

        var summary = new UserSummaryDto(user.Id, user.Email!, user.FirstName, user.LastName, user.Status.ToString(),
            roles.ToList(), user.ProfilePhotoPath is not null, user.SidebarOrder);
        return new AuthResponse(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt, summary);
    }

    private async Task<ApplicationUser> GetUserByRefreshTokenAsync(string refreshToken)
    {
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == refreshToken);

        return stored?.User ?? throw new ForbiddenException("Invalid refresh token.");
    }
}
