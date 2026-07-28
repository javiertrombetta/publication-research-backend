namespace PublicationSite.Api.Entities;

public class CoordinatorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    /// <summary>
    /// False while the coordinator is on leave/vacation/unavailable, excluding them
    /// from automatic assignment (fewest-students rule) without disabling their account.
    /// </summary>
    public bool IsAvailableForAssignment { get; set; } = true;

    public ICollection<PublicationContainer> AssignedContainers { get; set; } = [];
}
