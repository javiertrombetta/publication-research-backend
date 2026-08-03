using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Departments;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class DepartmentService(ApplicationDbContext db) : IDepartmentService
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
