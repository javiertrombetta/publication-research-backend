using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Containers;

namespace PublicationSite.Api.Services.Interfaces;

public interface IContainerService
{
    /// <summary>Student-initiated: creates the Container and auto-assigns a Coordinator by Department workload.</summary>
    Task<PublicationContainerDto> CreateAsync(Guid studentUserId, CancellationToken cancellationToken = default);

    Task<PublicationContainerDto> GetByIdAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default);

    /// <summary>Student-initiated: all of the acting student's own Containers, newest first. Empty when they haven't started any.</summary>
    Task<PagedResult<PublicationContainerDto>> GetMineAsync(Guid studentUserId, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Student-initiated: discards one of their own Containers created by mistake. Only allowed
    /// while it still holds no proposals. Once any proposal exists the process has started and the
    /// Container has to be resolved through the workflow instead.
    /// </summary>
    Task DeleteOwnAsync(Guid containerId, Guid studentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supervisor-initiated: the Containers this Supervisor has been assigned to, newest first.
    /// A Supervisor cannot list Containers any other way, and without this they have no way to
    /// find the ones waiting on their ethics decision or document review.
    /// </summary>
    Task<PagedResult<PublicationContainerDto>> GetSupervisingAsync(Guid supervisorUserId, ContainerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Head of Department-initiated: every Container belonging to a student in their Department,
    /// newest first. ContainerAccessService already lets them open any of these individually;
    /// this is how they find the ones waiting on their ethics review.
    /// </summary>
    Task<PagedResult<PublicationContainerDto>> GetInMyDepartmentAsync(Guid headOfDepartmentUserId, ContainerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything that has happened to this publication, newest first, one page at a time.
    /// </summary>
    Task<PagedResult<ActivityHistoryEntryDto>> GetActivityHistoryAsync(
        Guid id, Guid requestingUserId, PageRequest paging, CancellationToken cancellationToken = default);

    /// <summary>
    /// What this publication's trail can be filtered by: the actions it actually records and the
    /// people who appear in it.
    /// </summary>
    Task<ActivityHistoryFiltersDto> GetActivityHistoryFiltersAsync(
        Guid id, Guid requestingUserId, CancellationToken cancellationToken = default);

    Task<PagedResult<PublicationContainerDto>> GetAllAsync(ContainerQuery query, CancellationToken cancellationToken = default);

    /// <summary>Admin-only manual assignment; creates the Container if the student does not have one yet.</summary>
    Task<PublicationContainerDto> AssignCoordinatorManuallyAsync(AssignCoordinatorRequest request, Guid actingUserId, CancellationToken cancellationToken = default);
}
