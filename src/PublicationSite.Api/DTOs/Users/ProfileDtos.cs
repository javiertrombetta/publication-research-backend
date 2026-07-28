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

public record SupervisorProfileSummaryDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    string? AreasOfExpertise,
    string? ResearchInterests);

public record CoordinatorProfileSummaryDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    bool IsAvailableForAssignment);

public record HeadOfDepartmentProfileSummaryDto(Guid Id, Guid DepartmentId, string DepartmentName);

public record CommitteeMemberProfileSummaryDto(Guid Id, string Type, string? Affiliation);
