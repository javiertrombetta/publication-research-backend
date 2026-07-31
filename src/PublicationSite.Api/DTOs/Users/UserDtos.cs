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

/// <summary>
/// Granting a role to an account that already exists. Carries what the new role needs, because a
/// role without its profile is a role the person cannot actually use: a Coordinator with no
/// profile is invisible to auto-assignment, and a committee member with none cannot be put on a
/// committee at all.
/// </summary>
public record ChangeUserRoleRequest(
    string Role,
    string Comments,
    Guid? DepartmentId = null,
    string? Affiliation = null);

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
