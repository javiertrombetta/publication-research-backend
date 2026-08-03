namespace PublicationSite.Api.DTOs.Departments;

public record DepartmentDto(Guid Id, string Name, string Code, string? HeadOfDepartmentName);

public record CreateDepartmentRequest(string Name, string Code);

public record UpdateDepartmentRequest(string Name, string Code);

/// <summary>Somebody in a department, as a screen listing them needs them.</summary>
public record DepartmentPersonDto(Guid UserId, string Name, string Email);

/// <summary>
/// Who is in a department, by the job they do in it.
///
/// Two of these are the department's own: its heads and its coordinators belong to it and nowhere
/// else, so moving one is a change to this department. The other two are attachments rather than
/// posts, since a supervisor or a reviewer may be attached to several departments at once, and are
/// shown here so an administrator can see the whole of a department in one place.
/// </summary>
public record DepartmentMembersDto(
    Guid DepartmentId,
    string DepartmentName,
    IReadOnlyList<DepartmentPersonDto> HeadsOfDepartment,
    IReadOnlyList<DepartmentPersonDto> Coordinators,
    IReadOnlyList<DepartmentPersonDto> Supervisors,
    IReadOnlyList<DepartmentPersonDto> Reviewers);

/// <summary>
/// Who this department's heads and coordinators are, as a whole list.
///
/// Naming somebody moves them here from wherever they were. Leaving somebody out is not how they
/// are taken out: a head or a coordinator with no department holds a job in nothing, so the change
/// is refused and named, and the way out is to put them in another department or to change what
/// they are under Users.
/// </summary>
public record SetDepartmentMembersRequest(
    IReadOnlyList<Guid> HeadOfDepartmentUserIds,
    IReadOnlyList<Guid> CoordinatorUserIds);
