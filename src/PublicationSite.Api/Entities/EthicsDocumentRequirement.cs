namespace PublicationSite.Api.Entities;

/// <summary>
/// One document a student must supply at the ethics stage — the name an administrator gives it,
/// and where it sits in the list.
///
/// These were three fixed values in an enum. They are data now because the set is a matter of
/// institutional policy rather than of software: an ethics committee that adds a fourth form
/// should not need a deployment.
/// </summary>
public class EthicsDocumentRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Shown to the student under the name — what the document is, or where to get it.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Retired rather than deleted. A requirement that has been asked of anyone is referenced by
    /// their uploads and by the snapshot of every ethics approval that included it; removing the
    /// row would orphan documents already submitted in good faith. Inactive means "stop asking
    /// for this from now on", which is what retiring a form actually means.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<EthicsDocument> Documents { get; set; } = [];
}
