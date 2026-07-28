namespace PublicationSite.Api.Entities;

public class StudentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string StudentIdNumber { get; set; } = string.Empty;
    public string Programme { get; set; } = string.Empty;
    public string Cohort { get; set; } = string.Empty;

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public Guid? PreferredSupervisorId { get; set; }
    public ApplicationUser? PreferredSupervisor { get; set; }

    public string? Orcid { get; set; }

    public ICollection<ResearchArea> ResearchAreas { get; set; } = [];
}
