using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

/// <summary>
/// The student's Yes/No/Unsure ethics declaration for a Container. One-to-one:
/// the student may revisit "Unsure" repeatedly but only one final response is stored.
/// </summary>
public class EthicsDeclaration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public EthicsStudentResponse StudentResponse { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}
