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
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    ITokenService tokenService,
    IEmailSender emailSender,
    IAuditService auditService,
    IOptions<FrontendSettings> frontendOptions) : IAuthService
{
    private const string StudentEmailDomain = "@aisstudent.ac.nz";
    private const string StaffEmailDomain = "@ais.ac.nz";

    private readonly FrontendSettings _frontend = frontendOptions.Value;

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var role = ResolveRoleFromEmail(request.Email);

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

        await userManager.AddToRoleAsync(user, role);

        if (role == RoleNames.Student)
        {
            await CreateStudentProfileAsync(user, request, cancellationToken);
        }

        await SendEmailVerificationAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email)
            ?? throw new ForbiddenException("Invalid email or password.");

        var checkResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!checkResult.Succeeded)
        {
            throw new ForbiddenException(checkResult.IsLockedOut
                ? "Account is temporarily locked due to too many failed attempts."
                : "Invalid email or password.");
        }

        EnsureUserCanLogIn(user);

        var response = await BuildAuthResponseAsync(user);
        await auditService.LogAuditAsync(user.Id, "UserLoggedIn", nameof(ApplicationUser), user.Id);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var pair = await tokenService.RefreshAsync(refreshToken);
        var user = await GetUserByRefreshTokenAsync(pair.RefreshToken);
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

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

        await emailSender.SendAsync(email, "Reset your password",
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

        await auditService.LogAuditAsync(user.Id, "PasswordReset", nameof(ApplicationUser), user.Id);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new ValidationAppException(result.Errors.Select(e => e.Description).ToList());
        }

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
            var role = ResolveRoleFromEmail(email);
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
            await SendEmailVerificationAsync(user);

            throw new ForbiddenException("Account created. Please check your email to verify your address before logging in.");
        }

        EnsureUserCanLogIn(user);

        var response = await BuildAuthResponseAsync(user);
        await auditService.LogAuditAsync(user.Id, "UserLoggedInViaAzureSso", nameof(ApplicationUser), user.Id);
        return response;
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

    private async Task SendEmailVerificationAsync(ApplicationUser user)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        await emailSender.SendAsync(user.Email!, "Verify your email address",
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

    private static string ResolveRoleFromEmail(string email)
    {
        if (email.EndsWith(StudentEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return RoleNames.Student;
        }

        if (email.EndsWith(StaffEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return RoleNames.Staff;
        }

        throw new BusinessRuleException(
            $"Only '{StudentEmailDomain}' or '{StaffEmailDomain}' email addresses may register.");
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user, Services.Interfaces.TokenPair? pair = null)
    {
        var roles = await userManager.GetRolesAsync(user);
        pair ??= await tokenService.IssueTokensAsync(user, roles);

        var summary = new UserSummaryDto(user.Id, user.Email!, user.FirstName, user.LastName, user.Status.ToString(), roles.ToList());
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
