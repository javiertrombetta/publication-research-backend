using PublicationSite.Api.DTOs.Settings;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// The administrator's control over which documents the ethics stage asks for.
///
/// Changes reach publications opened afterwards only: each ethics approval keeps the list it was
/// given when documentation was first requested of it.
/// </summary>
public interface IEthicsDocumentRequirementService
{
    /// <summary>
    /// Every requirement, retired ones included, so an administrator can see the full history
    /// and bring one back rather than recreating it under a name that is already taken.
    /// </summary>
    Task<IReadOnlyList<EthicsDocumentRequirementDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<EthicsDocumentRequirementDto> CreateAsync(
        SaveEthicsDocumentRequirementRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<EthicsDocumentRequirementDto> UpdateAsync(
        Guid id, SaveEthicsDocumentRequirementRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires a requirement, or brings a retired one back. Never deletes: a requirement that
    /// has been asked of anyone is referenced by their uploads.
    /// </summary>
    Task<EthicsDocumentRequirementDto> SetActiveAsync(
        Guid id, bool isActive, Guid actingAdminId, CancellationToken cancellationToken = default);
}
