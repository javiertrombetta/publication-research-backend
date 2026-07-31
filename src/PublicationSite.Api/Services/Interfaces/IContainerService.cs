using PublicationSite.Api.DTOs.Containers;

namespace PublicationSite.Api.Services.Interfaces;

public interface IContainerService
{
    /// <summary>Student-initiated: creates the Container and auto-assigns a Coordinator by Department workload.</summary>
    Task<PublicationContainerDto> CreateAsync(Guid studentUserId, CancellationToken cancellationToken = default);

    Task<PublicationContainerDto> GetByIdAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>Student-initiated: all of the acting student's own Containers, newest first. Empty when they haven't started any.</summary>
    Task<IReadOnlyList<PublicationContainerDto>> GetMineAsync(Guid studentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Student-initiated: discards one of their own Containers created by mistake. Only allowed
    /// while it still holds no proposals — once any proposal exists the process has started and
    /// the Container has to be resolved through the workflow instead.
    /// </summary>
    Task DeleteOwnAsync(Guid containerId, Guid studentUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityHistoryEntryDto>> GetActivityHistoryAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicationContainerDto>> GetAllAsync(Guid? studentId, Guid? coordinatorId, string? status, CancellationToken cancellationToken = default);

    /// <summary>Admin-only manual assignment; creates the Container if the student does not have one yet.</summary>
    Task<PublicationContainerDto> AssignCoordinatorManuallyAsync(AssignCoordinatorRequest request, Guid actingUserId, CancellationToken cancellationToken = default);
}
