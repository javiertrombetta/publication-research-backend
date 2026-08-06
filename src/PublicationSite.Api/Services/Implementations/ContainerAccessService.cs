using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ContainerAccessService(ApplicationDbContext db) : IContainerAccessService
{
    /// <summary>
    /// One question, one query.
    ///
    /// This is asked on nearly every read in the system, and it used to answer itself in about
    /// five: fetch the user, ask whether they are an Admin, load the container, ask whether they
    /// are a Head of Department, then look for a committee seat. Every screen that enriched a list
    /// row by row paid that five times over per row, which is most of what made those pages slow.
    /// Expressed as a single predicate the database answers it in one pass and stops as soon as
    /// any branch matches.
    /// </summary>
    public async Task<bool> CanAccessAsync(Guid publicationContainerId, Guid userId) =>
        await WhereReadableBy(db.PublicationContainers.Where(c => c.Id == publicationContainerId), userId)
            .AnyAsync();

    public IQueryable<PublicationContainer> WhereReadableBy(
        IQueryable<PublicationContainer> containers, Guid userId)
    {
        var roles = db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name);

        return containers
            .Where(c =>
                // An Admin sees everything; the others see what is theirs.
                roles.Any(name => name == RoleNames.Admin)
                || c.StudentId == userId
                || c.CoordinatorId == userId
                || c.AssignedSupervisorId == userId
                // A Head of Department sees their own department's students, nobody else's.
                || (roles.Any(name => name == RoleNames.HeadOfDepartment)
                    && c.Student.StudentProfile != null
                    && db.HeadOfDepartmentProfiles.Any(h =>
                        h.UserId == userId && h.DepartmentId == c.Student.StudentProfile.DepartmentId))
                // And a committee member sees the publication they were appointed to.
                || db.CommitteeMembers.Any(m =>
                    m.UserId == userId
                    && m.Committee.Publication.PublicationContainerId == c.Id));
    }

    public async Task EnsureAccessAsync(Guid publicationContainerId, Guid userId)
    {
        if (!await CanAccessAsync(publicationContainerId, userId))
        {
            throw new ForbiddenException("You do not have access to this Publication Container.");
        }
    }
}
