namespace PublicationSite.Api.Entities;

public class SupervisorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public string? AreasOfExpertise { get; set; }
    public string? ResearchInterests { get; set; }
}
