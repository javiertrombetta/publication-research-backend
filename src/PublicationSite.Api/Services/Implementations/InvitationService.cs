using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class InvitationService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IUserProfileFactory profileFactory,
    IEmailSender emailSender,
    IAuditService auditService,
    ISystemSettingService settingService,
    IOptions<FrontendSettings> frontendOptions) : IInvitationService
{
    private readonly FrontendSettings _frontend = frontendOptions.Value;

    /// <summary>
    /// Roles that describe a person's place in a department, and so cannot be granted without
    /// saying which. External committee members are the deliberate exception: they belong to no
    /// department, which is the whole reason they have to be invited rather than registering.
    /// </summary>
    private static readonly string[] DepartmentRoles =
    [
        RoleNames.Student, RoleNames.Supervisor, RoleNames.Coordinator, RoleNames.HeadOfDepartment
    ];

    public async Task<IReadOnlyList<UserInvitationDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var invitations = await db.UserInvitations
            .AsNoTracking()
            .Include(i => i.Department)
            .Include(i => i.InvitedByUser)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. invitations.Select(ToDto)];
    }

    public async Task<UserInvitationDto> CreateAsync(
        CreateInvitationRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var email = NormaliseEmail(request.Email);

        if (!RoleNames.All.Contains(request.Role))
        {
            throw new BusinessRuleException($"'{request.Role}' is not a recognised role.");
        }

        if (DepartmentRoles.Contains(request.Role) && request.DepartmentId is null)
        {
            throw new BusinessRuleException($"A department is needed for the '{request.Role}' role.");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new BusinessRuleException("Give the person's name, so they know the invitation is meant for them.");
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException($"{email} already has an account.");
        }

        // Two live invitations to one address would mean two working links, and accepting the
        // second would fail on the account the first had already created.
        var existing = await db.UserInvitations
            .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (existing.Any(i => i.ExpiresAt > DateTime.UtcNow))
        {
            throw new ConflictException(
                $"{email} has already been invited and has not replied yet. Revoke that invitation, or send it again.");
        }

        var (token, hash) = GenerateToken();
        var validDays = (await settingService.GetAccessSettingsAsync(cancellationToken)).InvitationValidDays;

        var invitation = new UserInvitation
        {
            Email = email,
            Role = request.Role,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DepartmentId = DepartmentRoles.Contains(request.Role) ? request.DepartmentId : null,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(validDays),
            InvitedByUserId = actingAdminId
        };

        db.UserInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);

        // Recorded before the send, not after. The invitation row is already saved by this point,
        // and SendAsync throws when there is no working mail server, which used to skip this line
        // and leave a live invitation with nothing in the trail to say who created it or when.
        await auditService.LogAuditAsync(actingAdminId, "UserInvited", nameof(UserInvitation), invitation.Id,
            newValue: request.Role, comments: $"Invited {email} as {request.Role}.");

        await SendAsync(invitation, token, cancellationToken);

        return await ReloadAsync(invitation.Id, cancellationToken);
    }

    public async Task<UserInvitationDto> ResendAsync(Guid id, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var invitation = await FindAsync(id, cancellationToken);

        if (invitation.AcceptedAt is not null)
        {
            throw new BusinessRuleException("This invitation has already been accepted.");
        }

        if (invitation.RevokedAt is not null)
        {
            throw new BusinessRuleException("This invitation was withdrawn. Send a new one instead.");
        }

        // A fresh token, so the previous link stops working. Re-sending because an email went
        // astray should not leave two live ways in.
        var (token, hash) = GenerateToken();
        var validDays = (await settingService.GetAccessSettingsAsync(cancellationToken)).InvitationValidDays;

        invitation.TokenHash = hash;
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(validDays);
        await db.SaveChangesAsync(cancellationToken);

        // Before the send, as above: the token has already been replaced, so the previous link is
        // dead whether or not the new one reaches anyone. That is the part the trail must not lose.
        await auditService.LogAuditAsync(actingAdminId, "UserInvitationResent", nameof(UserInvitation), invitation.Id,
            comments: $"Sent again to {invitation.Email}. The previous link no longer works.");

        await SendAsync(invitation, token, cancellationToken);

        return await ReloadAsync(invitation.Id, cancellationToken);
    }

    public async Task<UserInvitationDto> RevokeAsync(Guid id, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var invitation = await FindAsync(id, cancellationToken);

        if (invitation.AcceptedAt is not null)
        {
            throw new BusinessRuleException(
                "This invitation has already been accepted. Disable or delete the account instead.");
        }

        if (invitation.RevokedAt is not null)
        {
            return await ReloadAsync(invitation.Id, cancellationToken);
        }

        invitation.RevokedAt = DateTime.UtcNow;
        invitation.RevokedByUserId = actingAdminId;

        // The link dies with the record. Leaving the hash in place would mean a withdrawn
        // invitation still worked for anyone holding the email.
        invitation.TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAuditAsync(actingAdminId, "UserInvitationRevoked", nameof(UserInvitation), invitation.Id,
            comments: $"Withdrew the invitation to {invitation.Email}.");

        return await ReloadAsync(invitation.Id, cancellationToken);
    }

    public async Task<InvitationPreviewDto> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindUsableByTokenAsync(token, cancellationToken);
        var institution = await settingService.GetInstitutionSettingsAsync(cancellationToken);

        return new InvitationPreviewDto(
            invitation.Email, invitation.Role, invitation.FirstName, invitation.LastName,
            institution.Name, invitation.ExpiresAt);
    }

    public async Task AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var invitation = await FindUsableByTokenAsync(request.Token, cancellationToken);

        // Between the invitation being sent and accepted, someone may have been given an account
        // another way. Creating a second one for the same address would break sign-in for both.
        if (await userManager.FindByEmailAsync(invitation.Email) is not null)
        {
            throw new ConflictException(
                "An account already exists for this address. Try signing in, or use 'I forgot password'.");
        }

        var user = new ApplicationUser
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            FirstName = invitation.FirstName,
            LastName = invitation.LastName,

            // Enabled and confirmed straight away: the invitation reached this address and the
            // person answered it, which is the same proof a verification email would give.
            Status = UserStatus.Enabled,
            EmailConfirmed = true,
            AuthProvider = AuthProvider.Local,
            PasswordChangedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            throw new ValidationAppException(createResult.Errors.Select(e => e.Description).ToList());
        }

        // From the invitation, never from the request. Otherwise accepting one would be a way to
        // award yourself any role you liked.
        await userManager.AddToRoleAsync(user, invitation.Role);

        await profileFactory.EnsureForRoleAsync(user, new CreateUserRequest
        {
            Email = invitation.Email,
            FirstName = invitation.FirstName,
            LastName = invitation.LastName,
            Role = invitation.Role,
            DepartmentId = invitation.DepartmentId,

            // A committee member's type follows from the role they were invited as.
            CommitteeMemberType = invitation.Role == RoleNames.Reviewer
                ? nameof(CommitteeMemberRoleType.Reviewer)
                : invitation.Role == RoleNames.ExternalCommitteeMember
                    ? nameof(CommitteeMemberRoleType.External)
                    : null
        }, cancellationToken);

        invitation.AcceptedAt = DateTime.UtcNow;

        // Spent, so the link cannot be used again, by the invitee or by anyone the email was
        // forwarded to.
        invitation.TokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAuditAsync(user.Id, "UserInvitationAccepted", nameof(UserInvitation), invitation.Id,
            newValue: invitation.Role, comments: $"{invitation.Email} accepted their invitation.",
            onBehalfOfUserId: invitation.InvitedByUserId);
    }

    // ---------- Helpers ----------

    private async Task SendAsync(UserInvitation invitation, string token, CancellationToken cancellationToken)
    {
        var institution = await settingService.GetInstitutionSettingsAsync(cancellationToken);
        var link = $"{_frontend.BaseUrl}/accept-invitation?token={Uri.EscapeDataString(token)}";
        var expires = invitation.ExpiresAt.ToString("d MMMM yyyy");

        var sent = await emailSender.SendAsync(invitation.Email,
            $"You have been invited to the {institution.Name} Research Publication Site",
            $"""
             <p>Hello {invitation.FirstName},</p>
             <p>You have been invited to the {institution.Name} Research Publication Site
             as {DisplayRole(invitation.Role)}.</p>
             <p><a href="{link}">Accept the invitation and set your password</a></p>
             <p>This link stops working on {expires}.</p>
             """,
            cancellationToken);

        // Said rather than swallowed: an invitation nobody receives looks identical to one
        // that is simply unanswered, and an administrator would wait for a reply that could
        // never come.
        if (!sent)
        {
            throw new BusinessRuleException(
                "The invitation was created but could not be emailed, because no working mail server is configured. " +
                "Set one up under System settings, then send it again.");
        }
    }

    /// <summary>
    /// Only the hash is stored, so the token in the email is the only copy. A leaked database
    /// therefore cannot be used to accept anyone's invitation.
    /// </summary>
    private static (string Token, string Hash) GenerateToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (token, Hash(token));
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<UserInvitation> FindUsableByTokenAsync(string token, CancellationToken cancellationToken)
    {
        // One message for a missing token, a mistyped one, and a spent one. They are the same thing
        // to the person holding the link, and distinguishing them would tell anyone guessing tokens
        // when they had found a real one.
        const string unusable =
            "This invitation link is not valid. It may already have been used, or been withdrawn. " +
            "Ask an administrator to send you a new one.";

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BusinessRuleException(unusable);
        }

        var invitation = await db.UserInvitations.FirstOrDefaultAsync(i => i.TokenHash == Hash(token), cancellationToken)
            ?? throw new BusinessRuleException(unusable);

        if (invitation.RevokedAt is not null)
        {
            throw new BusinessRuleException("This invitation has been withdrawn. Ask an administrator for a new one.");
        }

        if (invitation.AcceptedAt is not null)
        {
            throw new BusinessRuleException("This invitation has already been used. Try signing in instead.");
        }

        if (invitation.ExpiresAt <= DateTime.UtcNow)
        {
            throw new BusinessRuleException("This invitation has expired. Ask an administrator to send it again.");
        }

        return invitation;
    }

    private async Task<UserInvitation> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.UserInvitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(UserInvitation), id);

    private async Task<UserInvitationDto> ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var invitation = await db.UserInvitations
            .AsNoTracking()
            .Include(i => i.Department)
            .Include(i => i.InvitedByUser)
            .FirstAsync(i => i.Id == id, cancellationToken);

        return ToDto(invitation);
    }

    private static string NormaliseEmail(string? email)
    {
        var trimmed = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (trimmed.Length == 0 || !trimmed.Contains('@') || trimmed.StartsWith('@') || !trimmed.Contains('.'))
        {
            throw new BusinessRuleException($"'{email}' does not look like an email address.");
        }

        return trimmed;
    }

    private static UserInvitationDto ToDto(UserInvitation i) => new(
        i.Id, i.Email, i.Role, i.FirstName, i.LastName, i.DepartmentId, i.Department?.Name,
        i.InvitedByUser is null ? string.Empty : $"{i.InvitedByUser.FirstName} {i.InvitedByUser.LastName}",
        i.CreatedAt, i.ExpiresAt, i.AcceptedAt, i.RevokedAt,
        i.AcceptedAt is not null ? "Accepted"
        : i.RevokedAt is not null ? "Revoked"
        : i.ExpiresAt <= DateTime.UtcNow ? "Expired"
        : "Pending");

    private static string DisplayRole(string role) => role switch
    {
        RoleNames.HeadOfDepartment => "Head of Department",
        RoleNames.Reviewer => "a reviewer",
        RoleNames.ExternalCommitteeMember => "an external committee member",
        RoleNames.Admin => "an administrator",
        _ => "a " + role
    };
}
