namespace PublicationSite.Api.Entities;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StudentProfile> Students { get; set; } = [];
    public ICollection<SupervisorProfile> Supervisors { get; set; } = [];
    public ICollection<CoordinatorProfile> Coordinators { get; set; } = [];
    public HeadOfDepartmentProfile? HeadOfDepartment { get; set; }
}
