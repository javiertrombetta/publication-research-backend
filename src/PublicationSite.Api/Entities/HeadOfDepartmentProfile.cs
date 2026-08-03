namespace PublicationSite.Api.Entities;

/// <summary>
/// There is exactly one Head of Department per Department (enforced via a unique index on DepartmentId).
/// </summary>
public class HeadOfDepartmentProfile : IDepartmentPost
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
}
