namespace PublicationSite.Api.DTOs.Auth;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? InstitutionalId { get; set; }

    // Required only for @aisstudent.ac.nz addresses (auto-assigned the Student role).
    public string? StudentIdNumber { get; set; }
    public string? Programme { get; set; }
    public string? Cohort { get; set; }
    public Guid? DepartmentId { get; set; }
    public IReadOnlyList<Guid>? ResearchAreaIds { get; set; }
}
