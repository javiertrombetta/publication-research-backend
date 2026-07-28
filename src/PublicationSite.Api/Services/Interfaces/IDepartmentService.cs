using PublicationSite.Api.DTOs.Departments;

namespace PublicationSite.Api.Services.Interfaces;

public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DepartmentDto> CreateAsync(CreateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task SetCoordinatorAvailabilityAsync(Guid coordinatorUserId, bool isAvailable, CancellationToken cancellationToken = default);

    /// <summary>Picks the enabled, available Coordinator in the department currently supervising the fewest students.</summary>
    Task<Guid> SelectCoordinatorForDepartmentAsync(Guid departmentId, CancellationToken cancellationToken = default);
}
