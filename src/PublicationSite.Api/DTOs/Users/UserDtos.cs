namespace PublicationSite.Api.DTOs.Users;

public record UserListItemDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt);

public record UserDetailDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? InstitutionalId,
    string Status,
    string AuthProvider,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    object? Profile,
    bool HasProfilePhoto);

public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? InstitutionalId { get; set; }
    public string Role { get; set; } = string.Empty;

    public Guid? DepartmentId { get; set; }

    // Student
    public string? StudentIdNumber { get; set; }
    public string? Programme { get; set; }
    public string? Cohort { get; set; }
    public IReadOnlyList<Guid>? ResearchAreaIds { get; set; }

    // Supervisor
    public string? AreasOfExpertise { get; set; }
    public string? ResearchInterests { get; set; }

    // Committee member
    public string? CommitteeMemberType { get; set; }
    public string? Affiliation { get; set; }
}

public record UpdateUserRequest(string FirstName, string LastName, string? InstitutionalId, string Comments);

public record ChangeUserRoleRequest(string Role, string Comments);

public class ProfilePhotoUploadForm
{
    public IFormFile File { get; set; } = null!;
}

public record UpdateMyProfileRequest(
    string FirstName,
    string LastName,
    string? Programme,
    string? Cohort,
    Guid? PreferredSupervisorId,
    string? Orcid,
    IReadOnlyList<Guid>? ResearchAreaIds,
    string? AreasOfExpertise,
    string? ResearchInterests);
