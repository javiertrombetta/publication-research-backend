namespace PublicationSite.Api.DTOs.Departments;

public record DepartmentDto(Guid Id, string Name, string Code, string? HeadOfDepartmentName);

public record CreateDepartmentRequest(string Name, string Code);

public record UpdateDepartmentRequest(string Name, string Code);
