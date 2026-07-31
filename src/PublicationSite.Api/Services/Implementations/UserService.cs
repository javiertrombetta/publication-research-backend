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

namespace PublicationSite.Api.Services.Implementations;

public class UserService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IEmailSender emailSender,
    IAuditService auditService,
    IFileStorageService fileStorageService,
    IOptions<FrontendSettings> frontendOptions,
    IOptions<FileStorageSettings> fileStorageOptions) : IUserService
{
    private readonly FrontendSettings _frontend = frontendOptions.Value;
    private readonly FileStorageSettings _fileStorage = fileStorageOptions.Value;

    public async Task<IReadOnlyList<UserListItemDto>> GetAllAsync(string? role, string? status, string? search, CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<UserStatus>(status, true, out var statusFilter))
        {
            query = query.Where(u => u.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Email!.Contains(search) || u.FirstName.Contains(search) || u.LastName.Contains(search));
        }

        var users = await query.OrderBy(u => u.LastName).ToListAsync(cancellationToken);

        var results = new List<UserListItemDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (!string.IsNullOrWhiteSpace(role) && !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(new UserListItemDto(user.Id, user.Email!, user.FirstName, user.LastName,
                user.Status.ToString(), roles.ToList(), user.CreatedAt));
        }

        return results;
    }

    public async Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(id, cancellationToken);
        return await ToDetailDtoAsync(user, cancellationToken);
    }

    public async Task<UserDetailDto> CreateAsync(CreateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
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
        await CreateProfileForRoleAsync(user, request, cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{_frontend.BaseUrl}/set-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(user.Email!, "Set your password",
            $"<p>An administrator created an account for you on the AIS Research Publication Site.</p><p><a href=\"{link}\">Set your password</a></p>");

        await auditService.LogAuditAsync(actingAdminId, "UserCreated", nameof(ApplicationUser), user.Id,
            newValue: request.Role, onBehalfOfUserId: null);

        return await ToDetailDtoAsync(user, cancellationToken);
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
        await emailSender.SendAsync(user.Email!, "Your password was reset by an administrator",
            $"<p>An administrator reset your password. Use the link below to set a new one:</p><p><a href=\"{link}\">Set new password</a></p>");

        await auditService.LogAuditAsync(actingAdminId, "UserPasswordResetByAdmin", nameof(ApplicationUser), user.Id,
            comments: comments, onBehalfOfUserId: user.Id);
    }

    public async Task DeleteAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserOrThrowAsync(id, cancellationToken);

        await auditService.LogAuditAsync(actingAdminId, "UserDeleted", nameof(ApplicationUser), user.Id,
            comments: comments, onBehalfOfUserId: user.Id);

        await userManager.DeleteAsync(user);
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

        // Images only — deliberately not the document extension list, so a photo can't be a PDF
        // and an ethics document can't be a PNG.
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
            fileStorageService.Delete(previousPath);
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

            fileStorageService.Delete(path);

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
            user.ProfilePhotoPath is not null);
    }

    private async Task CreateProfileForRoleAsync(ApplicationUser user, CreateUserRequest request, CancellationToken cancellationToken)
    {
        switch (request.Role)
        {
            case RoleNames.Student:
                RequireDepartment(request);
                db.StudentProfiles.Add(new StudentProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value,
                    StudentIdNumber = request.StudentIdNumber ?? string.Empty,
                    Programme = request.Programme ?? string.Empty,
                    Cohort = request.Cohort ?? string.Empty,
                    ResearchAreas = request.ResearchAreaIds is { Count: > 0 }
                        ? await db.ResearchAreas.Where(r => request.ResearchAreaIds.Contains(r.Id)).ToListAsync(cancellationToken)
                        : []
                });
                break;

            case RoleNames.Supervisor:
                RequireDepartment(request);
                db.SupervisorProfiles.Add(new SupervisorProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value,
                    AreasOfExpertise = request.AreasOfExpertise,
                    ResearchInterests = request.ResearchInterests
                });
                break;

            case RoleNames.Coordinator:
                RequireDepartment(request);
                db.CoordinatorProfiles.Add(new CoordinatorProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value
                });
                break;

            case RoleNames.HeadOfDepartment:
                RequireDepartment(request);
                if (await db.HeadOfDepartmentProfiles.AnyAsync(h => h.DepartmentId == request.DepartmentId, cancellationToken))
                {
                    throw new ConflictException("This department already has a Head of Department assigned.");
                }
                db.HeadOfDepartmentProfiles.Add(new HeadOfDepartmentProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value
                });
                break;

            case RoleNames.InternalCommitteeMember:
            case RoleNames.ExternalCommitteeMember:
                db.CommitteeMemberProfiles.Add(new CommitteeMemberProfile
                {
                    UserId = user.Id,
                    Type = request.Role == RoleNames.InternalCommitteeMember
                        ? CommitteeMemberRoleType.Internal
                        : CommitteeMemberRoleType.External,
                    Affiliation = request.Affiliation
                });
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void RequireDepartment(CreateUserRequest request)
    {
        if (request.DepartmentId is null)
        {
            throw new BusinessRuleException($"A department is required for the '{request.Role}' role.");
        }
    }
}
