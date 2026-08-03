namespace PublicationSite.Api.DTOs.Common;

/// <param name="SidebarOrder">How this person has arranged their sidebar, as routes separated by spaces, or null if they never have. On this response because the site draws the menu on every page and cannot ask for it each time; it is put into the session at sign-in, which is also what keeps one person's arrangement off the next person to use the same browser.</param>
public record UserSummaryDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    IReadOnlyList<string> Roles,
    bool HasProfilePhoto,
    string? SidebarOrder = null);
