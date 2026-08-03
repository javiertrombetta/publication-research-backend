using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Departments;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class DepartmentService(ApplicationDbContext db, IAuditService auditService) : IDepartmentService
{
    public async Task<IReadOnlyList<DepartmentDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await db.Departments
            .Include(d => d.HeadsOfDepartment).ThenInclude(h => h.User)
            .OrderBy(d => d.Name)
            .Select(d => ToDto(d))
            .ToListAsync(cancellationToken);
    }

    public async Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments
            .Include(d => d.HeadsOfDepartment).ThenInclude(h => h.User)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        return ToDto(department);
    }

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        if (await db.Departments.AnyAsync(d => d.Code == request.Code, cancellationToken))
        {
            throw new ConflictException($"A department with code '{request.Code}' already exists.");
        }

        var department = new Department { Name = request.Name, Code = request.Code };
        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(department);
    }

    public async Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        department.Name = request.Name;
        department.Code = request.Code;
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(department);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        var inUse = await db.StudentProfiles.AnyAsync(s => s.DepartmentId == id, cancellationToken)
            || await db.DepartmentMemberships.AnyAsync(m => m.DepartmentId == id, cancellationToken)
            || await db.CoordinatorProfiles.AnyAsync(c => c.DepartmentId == id, cancellationToken);

        if (inUse)
        {
            throw new ConflictException("Cannot delete a department that still has students or staff assigned to it.");
        }

        db.Departments.Remove(department);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DepartmentMembersDto> GetMembersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        // The two posts, read from the profiles that hold them.
        var heads = await db.HeadOfDepartmentProfiles
            .Where(h => h.DepartmentId == id)
            .OrderBy(h => h.User.LastName).ThenBy(h => h.User.FirstName)
            .Select(h => new DepartmentPersonDto(h.UserId, h.User.FirstName + " " + h.User.LastName, h.User.Email!))
            .ToListAsync(cancellationToken);

        var coordinators = await db.CoordinatorProfiles
            .Where(c => c.DepartmentId == id)
            .OrderBy(c => c.User.LastName).ThenBy(c => c.User.FirstName)
            .Select(c => new DepartmentPersonDto(c.UserId, c.User.FirstName + " " + c.User.LastName, c.User.Email!))
            .ToListAsync(cancellationToken);

        // And the two attachments, which come from memberships because either may be in several
        // departments. Told apart by the role each holds rather than by the membership itself,
        // since one table carries both.
        var attached = await db.DepartmentMemberships
            .Where(m => m.DepartmentId == id)
            .OrderBy(m => m.User.LastName).ThenBy(m => m.User.FirstName)
            .Select(m => new
            {
                Person = new DepartmentPersonDto(m.UserId, m.User.FirstName + " " + m.User.LastName, m.User.Email!),
                Roles = db.UserRoles
                    .Where(ur => ur.UserId == m.UserId)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return new DepartmentMembersDto(
            department.Id,
            department.Name,
            heads,
            coordinators,
            [.. attached.Where(a => a.Roles.Contains(RoleNames.Supervisor)).Select(a => a.Person)],
            [.. attached.Where(a => a.Roles.Contains(RoleNames.Reviewer)).Select(a => a.Person)]);
    }

    public async Task<DepartmentMembersDto> SetMembersAsync(
        Guid id, SetDepartmentMembersRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), id);

        var heads = request.HeadOfDepartmentUserIds?.Distinct().ToList() ?? [];
        var coordinators = request.CoordinatorUserIds?.Distinct().ToList() ?? [];

        // Nobody holds both posts in the same department: they are two jobs, and one person doing
        // both is a decision to record as one of them rather than a tick in two boxes.
        var both = heads.Intersect(coordinators).ToList();
        if (both.Count > 0)
        {
            throw new BusinessRuleException(
                "Somebody is listed as both a head of department and a coordinator here. Choose one.");
        }

        await MoveAsync(db.HeadOfDepartmentProfiles, heads, RoleNames.HeadOfDepartment, "head of department", id, cancellationToken);
        await MoveAsync(db.CoordinatorProfiles, coordinators, RoleNames.Coordinator, "coordinator", id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAuditAsync(actingAdminId, "DepartmentMembersChanged", nameof(Department), id,
            comments: $"{department.Name} now has {heads.Count} head(s) of department and {coordinators.Count} coordinator(s).");

        return await GetMembersAsync(id, cancellationToken);
    }

    /// <summary>
    /// Puts the named people in this department, and refuses to leave anybody behind.
    ///
    /// Naming somebody moves their profile here from wherever it was, which is what makes this the
    /// one place a department is arranged. Leaving somebody out is not a way to remove them: a head
    /// or a coordinator with no department holds a job in nothing, and the sentence says how to do
    /// it properly instead of doing something surprising.
    /// </summary>
    private async Task MoveAsync<TProfile>(
        DbSet<TProfile> profiles, IReadOnlyList<Guid> wanted, string role, string label, Guid departmentId,
        CancellationToken cancellationToken)
        where TProfile : class, IDepartmentPost
    {
        var here = await profiles.Where(p => p.DepartmentId == departmentId).ToListAsync(cancellationToken);

        var leftOut = here.Where(p => !wanted.Contains(p.UserId)).Select(p => p.UserId).ToList();
        if (leftOut.Count > 0)
        {
            var names = await db.Users.Where(u => leftOut.Contains(u.Id))
                .Select(u => u.FirstName + " " + u.LastName)
                .ToListAsync(cancellationToken);

            throw new BusinessRuleException(
                $"{string.Join(", ", names)} would be left as a {label} of no department. "
                + "Put them in another department, or change what they are under Users.");
        }

        if (wanted.Count == 0) return;

        // Only people who already hold the role. Granting one is a separate decision made in the
        // user directory, where it asks for everything that role needs.
        var holders = await db.UserRoles
            .Where(ur => wanted.Contains(ur.UserId))
            .Join(db.Roles.Where(r => r.Name == role), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .ToListAsync(cancellationToken);

        var without = wanted.Except(holders).ToList();
        if (without.Count > 0)
        {
            var names = await db.Users.Where(u => without.Contains(u.Id))
                .Select(u => u.FirstName + " " + u.LastName)
                .ToListAsync(cancellationToken);

            throw new BusinessRuleException(
                $"{string.Join(", ", names)} is not a {label}. Make them one under Users first, which is where "
                + "the role is granted.");
        }

        var existing = await profiles.Where(p => wanted.Contains(p.UserId)).ToListAsync(cancellationToken);
        foreach (var profile in existing)
        {
            profile.DepartmentId = departmentId;
        }
    }

    public async Task SetCoordinatorAvailabilityAsync(Guid coordinatorUserId, bool isAvailable, CancellationToken cancellationToken = default)
    {
        var profile = await db.CoordinatorProfiles.FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, cancellationToken)
            ?? throw new NotFoundException(nameof(CoordinatorProfile), coordinatorUserId);

        profile.IsAvailableForAssignment = isAvailable;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> SelectCoordinatorForDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        // The role is checked as well as the profile. A profile outlives the role that created it.
        // They are never deleted, because Publication Containers point at them, so someone moved
        // off Coordinator would otherwise keep being handed new students.
        var coordinatorRoleId = await db.Roles
            .Where(r => r.Name == RoleNames.Coordinator)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var candidate = await db.CoordinatorProfiles
            // Two availability checks, because they mean different things. IsAvailableForAssignment
            // is the department's: an administrator steering new students away from a coordinator.
            // User.IsAvailable is the coordinator's own: they are on leave. Either one is enough to
            // pass somebody over.
            .Where(c => c.DepartmentId == departmentId && c.IsAvailableForAssignment
                        && c.User.IsAvailable
                        && c.User.Status == UserStatus.Enabled
                        && db.UserRoles.Any(ur => ur.UserId == c.UserId && ur.RoleId == coordinatorRoleId))
            .Select(c => new
            {
                c.UserId,
                ActiveCount = db.PublicationContainers.Count(container =>
                    container.CoordinatorId == c.UserId && container.Status == ContainerStatus.InProgress)
            })
            .OrderBy(c => c.ActiveCount)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            throw new BusinessRuleException("No available Coordinator is currently configured for this Department of Study.");
        }

        return candidate.UserId;
    }

    private static DepartmentDto ToDto(Department department) => new(
        department.Id,
        department.Name,
        department.Code,
        // The heads, as one line. Usually one name; a department the administrator has put two
        // people at the top of says both rather than picking one and hiding the other.
        department.HeadsOfDepartment.Count == 0
            ? null
            : string.Join(", ", department.HeadsOfDepartment
                .Where(h => h.User is not null)
                .Select(h => $"{h.User.FirstName} {h.User.LastName}")));
}
