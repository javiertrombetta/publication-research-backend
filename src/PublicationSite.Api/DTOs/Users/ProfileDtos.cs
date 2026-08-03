namespace PublicationSite.Api.DTOs.Users;

public record StudentProfileSummaryDto(
    Guid Id,
    string StudentIdNumber,
    string Programme,
    string Cohort,
    Guid DepartmentId,
    string DepartmentName,
    Guid? PreferredSupervisorId,
    string? Orcid,
    IReadOnlyList<string> ResearchAreas);

/// <param name="Departments">Every department this supervisor is attached to. A list because they may be in several, and empty is a real answer: somebody granted the role and not yet placed anywhere.</param>
public record SupervisorProfileSummaryDto(
    Guid Id,
    IReadOnlyList<DepartmentSummaryDto> Departments,
    string? AreasOfExpertise,
    string? ResearchInterests);

/// <summary>A department, named, where something only needs to say which one.</summary>
public record DepartmentSummaryDto(Guid Id, string Name);

public record CoordinatorProfileSummaryDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    bool IsAvailableForAssignment);

public record HeadOfDepartmentProfileSummaryDto(Guid Id, Guid DepartmentId, string DepartmentName);

public record CommitteeMemberProfileSummaryDto(Guid Id, string Type, string? Affiliation);
