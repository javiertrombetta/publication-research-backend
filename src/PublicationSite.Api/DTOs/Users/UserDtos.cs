namespace PublicationSite.Api.DTOs.Users;

/// <param name="IsAvailable">
/// Whether this person is taking work on. Set by them, and not the same as Status, which is set by
/// an administrator: disabled is an account that may not be used, unavailable is a person who is
/// working but should not be offered anything new.
/// </param>
public record UserListItemDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    bool IsAvailable = true);

/// <param name="DepartmentIds">The departments this person belongs to, for the roles that can be in several. Empty for everybody else, whose department is on their own profile, and for external committee members, who have none. An administrator changing somebody's role has to see what is already true, or the form offers to move them out of every department they are in.</param>
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
    bool HasProfilePhoto,
    bool IsAvailable = true,
    string? ThemePreference = null,
    IReadOnlyList<Guid>? DepartmentIds = null);

/// <summary>Light or dark. Anything else is refused rather than stored and puzzled over later.</summary>
public record UpdateThemeRequest(string Theme);

public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? InstitutionalId { get; set; }
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The department, for the roles that belong to exactly one: a student, a coordinator, a head
    /// of department.
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// The departments, for the roles that may belong to several: a supervisor and a reviewer.
    /// A single <see cref="DepartmentId"/> is accepted for those too and read as a list of one,
    /// so a caller with one department to give does not have to wrap it.
    /// </summary>
    public IReadOnlyList<Guid>? DepartmentIds { get; set; }

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
/// <param name="DepartmentId">The department, where the new role belongs to exactly one.</param>
/// <param name="DepartmentIds">The departments, where the new role may belong to several. A supervisor or a reviewer can be in more than one, and an external committee member is in none.</param>
public record ChangeUserRoleRequest(
    string Role,
    string Comments,
    Guid? DepartmentId = null,
    string? Affiliation = null,
    IReadOnlyList<Guid>? DepartmentIds = null);

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

/// <summary>Whether this person is currently taking work on.</summary>
public class SetAvailabilityRequest
{
    public bool IsAvailable { get; set; } = true;
}
