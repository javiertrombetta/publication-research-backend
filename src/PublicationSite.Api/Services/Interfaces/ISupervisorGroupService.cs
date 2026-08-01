using PublicationSite.Api.DTOs.Proposals;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// A coordinator's saved sets of supervisors, used to fill in the Send proposals form in one go.
///
/// Most methods take an owner, because a group belongs to the coordinator who made it and one
/// coordinator has no business reading or editing another's. Where that parameter is nullable,
/// null means an administrator is acting: they can see and tidy up everybody's, which is the only
/// way a group left behind by somebody who has moved on ever gets cleared away.
/// </summary>
public interface ISupervisorGroupService
{
    Task<IReadOnlyList<SupervisorGroupDto>> GetMineAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Every coordinator's groups, for an administrator. Optionally narrowed by group name, owner or member.</summary>
    Task<IReadOnlyList<SupervisorGroupDto>> GetAllAsync(
        string? search = null, CancellationToken cancellationToken = default);

    Task<SupervisorGroupDto> CreateAsync(
        Guid ownerId, SaveSupervisorGroupRequest request, CancellationToken cancellationToken = default);

    /// <param name="ownerId">The coordinator making the change, or null when an administrator is.</param>
    Task<SupervisorGroupDto> UpdateAsync(
        Guid groupId, Guid? ownerId, SaveSupervisorGroupRequest request, CancellationToken cancellationToken = default);

    /// <param name="ownerId">The coordinator making the change, or null when an administrator is.</param>
    Task DeleteAsync(Guid groupId, Guid? ownerId, CancellationToken cancellationToken = default);

    /// <summary>Discards the groups named, whoever they belong to. For an administrator.</summary>
    /// <returns>How many were actually discarded.</returns>
    Task<int> DeleteManyAsync(IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken = default);

    /// <summary>Discards every group in the institution. For an administrator, and a separate method from the one above so it cannot happen by passing an empty list.</summary>
    /// <returns>How many were discarded.</returns>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}
