using PublicationSite.Api.DTOs.Departments;

namespace PublicationSite.Api.Services.Interfaces;

public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Everybody attached to a department, by the job they do in it.</summary>
    Task<DepartmentMembersDto> GetMembersAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets this department's heads and coordinators. Naming somebody moves them here; leaving
    /// somebody out is refused rather than stranding them with a job in no department.
    /// </summary>
    Task<DepartmentMembersDto> SetMembersAsync(
        Guid id, SetDepartmentMembersRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task SetCoordinatorAvailabilityAsync(Guid coordinatorUserId, bool isAvailable, CancellationToken cancellationToken = default);

    /// <summary>Picks the enabled, available Coordinator in the department currently supervising the fewest students.</summary>
    Task<Guid> SelectCoordinatorForDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);
}
