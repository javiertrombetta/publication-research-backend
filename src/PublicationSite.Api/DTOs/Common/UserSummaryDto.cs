namespace PublicationSite.Api.DTOs.Common;

public record UserSummaryDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    IReadOnlyList<string> Roles);
