using PublicationSite.Api.DTOs.Containers;

namespace PublicationSite.Api.Services.Interfaces;

public interface IContainerService
{
    /// <summary>Student-initiated: creates the Container and auto-assigns a Coordinator by Department workload.</summary>
    Task<PublicationContainerDto> CreateAsync(Guid studentUserId, CancellationToken cancellationToken = default);

    Task<PublicationContainerDto> GetByIdAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityHistoryEntryDto>> GetActivityHistoryAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicationContainerDto>> GetAllAsync(Guid? studentId, Guid? coordinatorId, string? status, CancellationToken cancellationToken = default);

    /// <summary>Admin-only manual assignment; creates the Container if the student does not have one yet.</summary>
    Task<PublicationContainerDto> AssignCoordinatorManuallyAsync(AssignCoordinatorRequest request, Guid actingUserId, CancellationToken cancellationToken = default);
}
