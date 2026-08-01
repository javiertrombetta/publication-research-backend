namespace PublicationSite.Api.Entities;

/// <summary>
/// The list of documents one particular ethics approval asks for, fixed at the moment the
/// documentation was requested.
///
/// Without this snapshot, an administrator adding a fourth required form would silently reopen
/// every ethics stage already completed under the old list of three. Students would find themselves
/// incomplete against a rule that did not exist when they finished. The snapshot is what makes a
/// change apply to new work only.
/// </summary>
public class EthicsApprovalRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EthicsApprovalId { get; set; }
    public EthicsApproval EthicsApproval { get; set; } = null!;

    public Guid EthicsDocumentRequirementId { get; set; }
    public EthicsDocumentRequirement EthicsDocumentRequirement { get; set; } = null!;

    /// <summary>
    /// Copied from the requirement so the student's list keeps the order it was presented in,
    /// even if an administrator later reorders the master list.
    /// </summary>
    public int SortOrder { get; set; }
}
