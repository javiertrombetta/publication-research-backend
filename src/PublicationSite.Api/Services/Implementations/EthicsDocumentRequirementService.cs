using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class EthicsDocumentRequirementService(ApplicationDbContext db, IAuditService auditService)
    : IEthicsDocumentRequirementService
{
    public async Task<IReadOnlyList<EthicsDocumentRequirementDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.EthicsDocumentRequirements
            .AsNoTracking()
            .OrderBy(r => r.IsActive ? 0 : 1)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Name)
            .Select(r => new EthicsDocumentRequirementDto(
                r.Id, r.Name, r.Description, r.SortOrder, r.IsActive,
                // Whether anyone has been asked for it. Drives whether the administrator is offered
                // "retire" or "delete". A requirement nobody has used is safe to remove.
                r.Documents.Any() || db.EthicsApprovalRequirements.Any(a => a.EthicsDocumentRequirementId == r.Id)))
            .ToListAsync(cancellationToken);

    public async Task<EthicsDocumentRequirementDto> CreateAsync(
        SaveEthicsDocumentRequirementRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var name = Normalise(request.Name);
        await EnsureNameIsFreeAsync(name, null, cancellationToken);

        var requirement = new EthicsDocumentRequirement
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true
        };

        db.EthicsDocumentRequirements.Add(requirement);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAuditAsync(actingAdminId, "EthicsDocumentRequirementCreated",
            nameof(EthicsDocumentRequirement), requirement.Id, newValue: name,
            comments: "Will be asked of publications whose ethics stage starts from now on.");

        return ToDto(requirement, isInUse: false);
    }

    public async Task<EthicsDocumentRequirementDto> UpdateAsync(
        Guid id, SaveEthicsDocumentRequirementRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var requirement = await FindAsync(id, cancellationToken);
        var name = Normalise(request.Name);
        await EnsureNameIsFreeAsync(name, id, cancellationToken);

        var previousName = requirement.Name;

        // Renaming is deliberately allowed even once documents exist. Uploads point at the row, not
        // the text, so correcting a form's title does not detach anything already submitted, and a
        // typo in a document name is exactly the kind of thing worth being able to fix.
        requirement.Name = name;
        requirement.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        requirement.SortOrder = request.SortOrder;
        requirement.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        if (!string.Equals(previousName, name, StringComparison.Ordinal))
        {
            await auditService.LogAuditAsync(actingAdminId, "EthicsDocumentRequirementRenamed",
                nameof(EthicsDocumentRequirement), requirement.Id, previousValue: previousName, newValue: name);
        }

        return ToDto(requirement, await IsInUseAsync(id, cancellationToken));
    }

    public async Task<EthicsDocumentRequirementDto> SetActiveAsync(
        Guid id, bool isActive, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var requirement = await FindAsync(id, cancellationToken);

        // Leaving nothing active would strand the next student: the ethics stage would ask them
        // for a list of no documents and never be able to complete.
        if (!isActive)
        {
            var othersActive = await db.EthicsDocumentRequirements
                .CountAsync(r => r.IsActive && r.Id != id, cancellationToken);

            if (othersActive == 0)
            {
                throw new BusinessRuleException(
                    "At least one ethics document must remain in use. Add its replacement before retiring this one.");
            }
        }

        requirement.IsActive = isActive;
        requirement.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogAuditAsync(actingAdminId,
            isActive ? "EthicsDocumentRequirementRestored" : "EthicsDocumentRequirementRetired",
            nameof(EthicsDocumentRequirement), requirement.Id, newValue: requirement.Name,
            comments: isActive
                ? "Will be asked for again from now on."
                : "Will no longer be asked for. Publications already asked for it still owe it.");

        return ToDto(requirement, await IsInUseAsync(id, cancellationToken));
    }

    // ---------- Helpers ----------

    private async Task<EthicsDocumentRequirement> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.EthicsDocumentRequirements.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(EthicsDocumentRequirement), id);

    private static string Normalise(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        return trimmed.Length switch
        {
            0 => throw new BusinessRuleException("Give the document a name."),
            > 200 => throw new BusinessRuleException("A document name cannot be longer than 200 characters."),
            _ => trimmed
        };
    }

    /// <summary>
    /// Checked here as well as by the unique index, so the administrator gets a sentence rather
    /// than a database error. Retired requirements count: reusing a retired name would make two
    /// different documents indistinguishable in the history.
    /// </summary>
    private async Task EnsureNameIsFreeAsync(string name, Guid? excludingId, CancellationToken cancellationToken)
    {
        var taken = await db.EthicsDocumentRequirements
            .AnyAsync(r => r.Name == name && (excludingId == null || r.Id != excludingId), cancellationToken);

        if (taken)
        {
            throw new ConflictException($"There is already a document called '{name}'.");
        }
    }

    private Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        db.EthicsDocuments.AnyAsync(d => d.EthicsDocumentRequirementId == id, cancellationToken);

    private static EthicsDocumentRequirementDto ToDto(EthicsDocumentRequirement r, bool isInUse) =>
        new(r.Id, r.Name, r.Description, r.SortOrder, r.IsActive, isInUse);
}
