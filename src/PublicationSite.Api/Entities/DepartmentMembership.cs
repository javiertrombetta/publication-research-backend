namespace PublicationSite.Api.Entities;

/// <summary>
/// One person, one department they belong to.
///
/// Supervising and reviewing are not exclusive: somebody can supervise for Information Technology
/// and sit on Business committees in the same year, and an institution that made them choose would
/// be describing its own staffing wrongly. So those two roles carry a set of departments rather
/// than a field, which is what this table is.
///
/// The other roles keep the single department on their own profile, because for them it is not a
/// list: heading a department and coordinating one are jobs in that department, and a person doing
/// either in two places holds two of those jobs. And an external committee member has none at all,
/// which is the point of them: they come from another institution.
/// </summary>
public class DepartmentMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
