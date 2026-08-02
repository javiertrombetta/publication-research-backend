using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Services.Implementations;

public class UserService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IEmailSender emailSender,
    IAuditService auditService,
    IFileStorageService fileStorageService,
    IUserProfileFactory profileFactory,
    IOptions<FrontendSettings> frontendOptions,
    IOptions<FileStorageSettings> fileStorageOptions) : IUserService
{
    private readonly FrontendSettings _frontend = frontendOptions.Value;
    private readonly FileStorageSettings _fileStorage = fileStorageOptions.Value;

    /// <summary>What the directory can be ordered by. Surname first, as it is displayed.</summary>
    private static readonly Dictionary<string, Expression<Func<ApplicationUser, object?>>> DirectorySorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = u => u.LastName,
            ["email"] = u => u.Email,
            ["status"] = u => u.Status,
            ["created"] = u => u.CreatedAt
        };

    public async Task<PagedResult<UserListItemDto>> GetAllAsync(
        string? role, string? status, string? search, PageRequest paging, bool availableOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsQueryable();

        // Asked for wherever the list is a list of candidates for something rather than a
        // directory. Somebody who has said they are not taking work on should not appear in a
        // chooser: sending them a proposal is asking a question nobody is there to answer.
        if (availableOnly) query = query.Where(u => u.IsAvailable);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<UserStatus>(status, true, out var statusFilter))
        {
            query = query.Where(u => u.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Email!.Contains(search) || u.FirstName.Contains(search) || u.LastName.Contains(search));
        }

        // Filtered in the database rather than after loading everybody. The role a person holds
        // lives in another table, and asking UserManager for it means a query per user, so this
        // used to read every account in the institution, ask sixty-odd questions to find out what
        // they were, and throw away all but the two it had been asked for. The work was identical
        // whether the caller wanted one role or all of them.
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => db.UserRoles
                .Where(ur => ur.UserId == u.Id)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .Any(name => name == role));
        }

        var total = await query.CountAsync(cancellationToken);

        var ordered = paging.SortBy is not null && DirectorySorts.TryGetValue(paging.SortBy, out var key)
            ? paging.SortDescending ? query.OrderByDescending(key) : query.OrderBy(key)
            : query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName);

        var items = await ordered
            .Skip((paging.SafePage - 1) * paging.SafePageSize)
            .Take(paging.SafePageSize)
            .Select(u => new UserListItemDto(
                u.Id, u.Email!, u.FirstName, u.LastName, u.Status.ToString(),
                db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .ToList(),
                u.CreatedAt,
                u.IsAvailable))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserListItemDto>(items, paging.SafePage, paging.SafePageSize, total);
    }

    /// <summary>
    /// Records which theme this person prefers. Refuses anything but the two that exist, so a
    /// stored value is always one the site can actually draw.
    /// </summary>
    public async Task SetThemeAsync(Guid userId, string theme, CancellationToken cancellationToken = default)
    {
        if (theme is not ("light" or "dark"))
        {
            throw new BusinessRuleException("A theme is either light or dark.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        user.ThemePreference = theme;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(id, cancellationToken);
        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task<(UserDetailDto User, bool PasswordEmailSent)> CreateAsync(CreateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (!RoleNames.All.Contains(request.Role))
        {
            throw new BusinessRuleException($"'{request.Role}' is not a recognised role.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            InstitutionalId = request.InstitutionalId,
            Status = UserStatus.Enabled,
            AuthProvider = AuthProvider.Local,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            throw new ValidationAppException(createResult.Errors.Select(e => e.Description).ToList());
        }

        await userManager.AddToRoleAsync(user, request.Role);
        await profileFactory.EnsureForRoleAsync(user, request, cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/set-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        var sent = await emailSender.SendAsync(user.Email!, "Set your password",
            $"<p>An administrator created an account for you on the AIS Research Publication Site.</p><p><a href=\"{link}\">Set your password</a></p>");

        await auditService.LogAuditAsync(actingAdminId, "UserCreated", nameof(ApplicationUser), user.Id,
            newValue: request.Role, onBehalfOfUserId: null);

        return (await ToDetailDtoAsync(user, cancellationToken), sent);
    }

    public async Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(id, cancellationToken);

        var previous = $"{user.FirstName} {user.LastName}";
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.InstitutionalId = request.InstitutionalId;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await auditService.LogAuditAsync(actingAdminId, "UserUpdated", nameof(ApplicationUser), user.Id,
            previous, $"{request.FirstName} {request.LastName}", request.Comments, onBehalfOfUserId: user.Id);

        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (!RoleNames.All.Contains(request.Role))
        {
            throw new BusinessRuleException($"'{request.Role}' is not a recognised role.");
        }

        var user = await FindUserOrThrowAsync(id, cancellationToken);
        var currentRoles = await userManager.GetRolesAsync(user);

        // The profile comes first. If the role needs a department and none was given, this throws
        // before the role is swapped. Better than leaving the account holding a role it cannot use,
        // which is what happened when the role was changed on its own.
        await profileFactory.EnsureForRoleAsync(user, new CreateUserRequest
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = request.Role,
            DepartmentId = request.DepartmentId,
            Affiliation = request.Affiliation
        }, cancellationToken);

        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, request.Role);

        await auditService.LogAuditAsync(actingAdminId, "UserRoleChanged", nameof(ApplicationUser), user.Id,
            string.Join(",", currentRoles), request.Role, request.Comments, onBehalfOfUserId: user.Id);
    }

    public async Task EnableAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default) =>
        await SetStatusAsync(id, UserStatus.Enabled, "UserEnabled", comments, actingAdminId, cancellationToken);

    public async Task DisableAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default) =>
        await SetStatusAsync(id, UserStatus.Disabled, "UserDisabled", comments, actingAdminId, cancellationToken);

    public async Task ResetPasswordAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(id, cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        var sent = await emailSender.SendAsync(user.Email!, "Your password was reset by an administrator",
            $"<p>An administrator reset your password. Use the link below to set a new one:</p><p><a href=\"{link}\">Set new password</a></p>");

        await auditService.LogAuditAsync(actingAdminId, "UserPasswordResetByAdmin", nameof(ApplicationUser), user.Id,
            comments: comments, onBehalfOfUserId: user.Id);

        if (!sent)
        {
            throw new BusinessRuleException(
                $"Could not email the reset link to {user.Email}, because no working mail server is configured. " +
                "Set one up under System settings, then try again.");
        }
    }

    public async Task DeleteAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comments))
        {
            throw new BusinessRuleException("A reason is required when deleting an account.");
        }

        if (id == actingAdminId)
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        var user = await FindUserOrThrowAsync(id, cancellationToken);

        // Recorded before anything is stripped, so the trail still says who this was.
        await auditService.LogAuditAsync(actingAdminId, "UserDeleted", nameof(ApplicationUser), user.Id,
            previousValue: user.Email, comments: comments, onBehalfOfUserId: user.Id);

        if (user.ProfilePhotoPath is { } photoPath)
        {
            await fileStorageService.DeleteAsync(photoPath, cancellationToken);
            user.ProfilePhotoPath = null;
        }

        // A placeholder address rather than null: Identity requires a user name, and .invalid is
        // reserved by RFC 2606 so nothing can ever be delivered to it. The id keeps it unique.
        var placeholder = $"deleted-{user.Id}@deleted.invalid";

        user.Email = placeholder;
        user.NormalizedEmail = placeholder.ToUpperInvariant();
        user.UserName = placeholder;
        user.NormalizedUserName = placeholder.ToUpperInvariant();
        user.FirstName = "Deleted";
        user.LastName = "account";
        user.InstitutionalId = null;
        user.PhoneNumber = null;
        user.EmailConfirmed = false;
        user.Status = UserStatus.Disabled;
        user.UpdatedAt = DateTime.UtcNow;

        // Locks the account out for good and invalidates any token already issued to it.
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<UserDetailDto> GetOwnProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(userId, cancellationToken);
        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task<UserDetailDto> SetOwnProfilePhotoAsync(
        Guid userId, Stream content, string fileName, long lengthBytes, CancellationToken cancellationToken = default)
    {
        if (lengthBytes <= 0)
        {
            throw new BusinessRuleException("The selected file is empty.");
        }

        if (lengthBytes > _fileStorage.MaxProfilePhotoBytes)
        {
            var limitMb = _fileStorage.MaxProfilePhotoBytes / (1024 * 1024);
            throw new BusinessRuleException($"Profile photos must be {limitMb} MB or smaller.");
        }

        var user = await FindUserOrThrowAsync(userId, cancellationToken);

        // Images only, deliberately not the document extension list, so a photo can't be a PDF and
        // an ethics document can't be a PNG.
        var stored = await fileStorageService.SaveAsync(
            content, fileName, $"profile-photos/{userId}", _fileStorage.AllowedImageExtensions, cancellationToken);

        var previousPath = user.ProfilePhotoPath;
        user.ProfilePhotoPath = stored.RelativePath;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Only after the record points at the new file, so a failure never leaves the user
        // referencing a file that no longer exists.
        if (previousPath is not null)
        {
            await fileStorageService.DeleteAsync(previousPath, cancellationToken);
        }

        await auditService.LogAuditAsync(userId, "ProfilePhotoUpdated", nameof(ApplicationUser), userId,
            previousValue: previousPath, newValue: stored.RelativePath);

        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task<UserDetailDto> RemoveOwnProfilePhotoAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(userId, cancellationToken);

        if (user.ProfilePhotoPath is { } path)
        {
            user.ProfilePhotoPath = null;
            user.UpdatedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);

            await fileStorageService.DeleteAsync(path, cancellationToken);

            await auditService.LogAuditAsync(userId, "ProfilePhotoRemoved", nameof(ApplicationUser), userId,
                previousValue: path);
        }

        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType)> OpenProfilePhotoAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(userId, cancellationToken);

        if (user.ProfilePhotoPath is not { } path)
        {
            throw new NotFoundException("Profile photo", userId);
        }

        var stream = await fileStorageService.OpenReadAsync(path, cancellationToken);
        return (stream, ContentTypeFor(path));
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    public async Task<UserDetailDto> UpdateOwnProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(userId, cancellationToken);
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        var studentProfile = await db.StudentProfiles.Include(s => s.ResearchAreas)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (studentProfile is not null)
        {
            studentProfile.Programme = request.Programme ?? studentProfile.Programme;
            studentProfile.Cohort = request.Cohort ?? studentProfile.Cohort;
            studentProfile.PreferredSupervisorId = request.PreferredSupervisorId ?? studentProfile.PreferredSupervisorId;
            studentProfile.Orcid = request.Orcid ?? studentProfile.Orcid;

            if (request.ResearchAreaIds is not null)
            {
                var areas = await db.ResearchAreas.Where(r => request.ResearchAreaIds.Contains(r.Id)).ToListAsync(cancellationToken);
                studentProfile.ResearchAreas = areas;
            }
        }

        var supervisorProfile = await db.SupervisorProfiles.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
        if (supervisorProfile is not null)
        {
            supervisorProfile.AreasOfExpertise = request.AreasOfExpertise ?? supervisorProfile.AreasOfExpertise;
            supervisorProfile.ResearchInterests = request.ResearchInterests ?? supervisorProfile.ResearchInterests;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task SetAvailabilityAsync(Guid userId, bool isAvailable, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(userId, cancellationToken);

        // Only somebody with a job here can be available for it. An account still holding the
        // placeholder role is not chosen for anything, so its availability governs nothing, and
        // offering the setting would be a promise the system never keeps. Refused rather than
        // ignored: a control that appears to work and does nothing is worse than one that is not
        // there, and the screen does not show it either.
        var roles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync(cancellationToken);

        if (!roles.Any(RoleNames.Operational.Contains))
        {
            throw new BusinessRuleException(
                "This account has no role here yet, so there is nothing to be available for. "
                + "An administrator grants a role first.");
        }

        if (user.IsAvailable == isAvailable) return;

        user.IsAvailable = isAvailable;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // On the trail, because it explains gaps in the record: a supervisor who received nothing
        // for a month was not being passed over, they had said they were not available.
        await auditService.LogAuditAsync(userId, isAvailable ? "MarkedAvailable" : "MarkedUnavailable",
            nameof(ApplicationUser), userId,
            previousValue: (!isAvailable).ToString(),
            newValue: isAvailable.ToString(),
            comments: isAvailable
                ? "Available for new work again."
                : "Not taking new work on. Existing work is unaffected.");
    }

    private async Task SetStatusAsync(Guid id, UserStatus status, string actionType, string comments, Guid actingAdminId, CancellationToken cancellationToken)
    {
        var user = await FindUserOrThrowAsync(id, cancellationToken);
        var previous = user.Status.ToString();
        user.Status = status;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await auditService.LogAuditAsync(actingAdminId, actionType, nameof(ApplicationUser), user.Id,
            previous, status.ToString(), comments, onBehalfOfUserId: user.Id);
    }

    private async Task<ApplicationUser> FindUserOrThrowAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), id);
    }

    private async Task<UserDetailDto> ToDetailDtoAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);

        object? profile = null;
        if (roles.Contains(RoleNames.Student))
        {
            profile = await db.StudentProfiles.Where(s => s.UserId == user.Id)
                .Select(s => new StudentProfileSummaryDto(s.Id, s.StudentIdNumber, s.Programme, s.Cohort,
                    s.DepartmentId, s.Department.Name, s.PreferredSupervisorId, s.Orcid,
                    s.ResearchAreas.Select(r => r.Name).ToList()))
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (roles.Contains(RoleNames.Supervisor))
        {
            profile = await db.SupervisorProfiles.Where(s => s.UserId == user.Id)
                .Select(s => new SupervisorProfileSummaryDto(s.Id, s.DepartmentId, s.Department.Name, s.AreasOfExpertise, s.ResearchInterests))
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (roles.Contains(RoleNames.Coordinator))
        {
            profile = await db.CoordinatorProfiles.Where(c => c.UserId == user.Id)
                .Select(c => new CoordinatorProfileSummaryDto(c.Id, c.DepartmentId, c.Department.Name, c.IsAvailableForAssignment))
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (roles.Contains(RoleNames.HeadOfDepartment))
        {
            profile = await db.HeadOfDepartmentProfiles.Where(h => h.UserId == user.Id)
                .Select(h => new HeadOfDepartmentProfileSummaryDto(h.Id, h.DepartmentId, h.Department.Name))
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (roles.Contains(RoleNames.InternalCommitteeMember) || roles.Contains(RoleNames.ExternalCommitteeMember))
        {
            profile = await db.CommitteeMemberProfiles.Where(c => c.UserId == user.Id)
                .Select(c => new CommitteeMemberProfileSummaryDto(c.Id, c.Type.ToString(), c.Affiliation))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new UserDetailDto(user.Id, user.Email!, user.FirstName, user.LastName, user.InstitutionalId,
            user.Status.ToString(), user.AuthProvider.ToString(), roles.ToList(), user.CreatedAt, profile,
            user.ProfilePhotoPath is not null, user.IsAvailable, user.ThemePreference);
    }

}
