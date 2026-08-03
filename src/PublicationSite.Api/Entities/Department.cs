namespace PublicationSite.Api.Entities;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StudentProfile> Students { get; set; } = [];
    public ICollection<CoordinatorProfile> Coordinators { get; set; } = [];

    /// <summary>
    /// The heads of this department. One is the usual answer and the default the screens offer,
    /// but it is a collection because the administrator decides: a department between two people,
    /// or handing over across a term, is a real arrangement and not an error to be refused.
    /// </summary>
    public ICollection<HeadOfDepartmentProfile> HeadsOfDepartment { get; set; } = [];

    /// <summary>The supervisors and reviewers attached to it, each of whom may be in others too.</summary>
    public ICollection<DepartmentMembership> Memberships { get; set; } = [];
}
