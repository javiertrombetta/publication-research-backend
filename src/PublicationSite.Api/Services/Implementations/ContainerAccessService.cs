using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ContainerAccessService(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : IContainerAccessService
{
    public async Task<bool> CanAccessAsync(Guid publicationContainerId, Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        if (await userManager.IsInRoleAsync(user, RoleNames.Admin))
        {
            return true;
        }

        var container = await db.PublicationContainers
            .Include(c => c.Student).ThenInclude(s => s.StudentProfile)
            .FirstOrDefaultAsync(c => c.Id == publicationContainerId);

        if (container is null)
        {
            return false;
        }

        if (container.StudentId == userId || container.CoordinatorId == userId || container.AssignedSupervisorId == userId)
        {
            return true;
        }

        if (await userManager.IsInRoleAsync(user, RoleNames.HeadOfDepartment))
        {
            var studentDepartmentId = container.Student.StudentProfile?.DepartmentId;
            var isHeadOfThatDepartment = studentDepartmentId is not null && await db.HeadOfDepartmentProfiles
                .AnyAsync(h => h.UserId == userId && h.DepartmentId == studentDepartmentId);

            if (isHeadOfThatDepartment)
            {
                return true;
            }
        }

        var isCommitteeMember = await db.CommitteeMembers
            .AnyAsync(m => m.UserId == userId && m.Committee.Publication.PublicationContainerId == publicationContainerId);

        return isCommitteeMember;
    }

    public async Task EnsureAccessAsync(Guid publicationContainerId, Guid userId)
    {
        if (!await CanAccessAsync(publicationContainerId, userId))
        {
            throw new ForbiddenException("You do not have access to this Publication Container.");
        }
    }
}
