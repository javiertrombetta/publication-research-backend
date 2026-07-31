using Microsoft.AspNetCore.Identity;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? InstitutionalId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;
    public string? AzureObjectId { get; set; }

    /// <summary>Storage-relative path of the user's profile photo; null when they have none.</summary>
    public string? ProfilePhotoPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public StudentProfile? StudentProfile { get; set; }
    public SupervisorProfile? SupervisorProfile { get; set; }
    public CoordinatorProfile? CoordinatorProfile { get; set; }
    public HeadOfDepartmentProfile? HeadOfDepartmentProfile { get; set; }
    public CommitteeMemberProfile? CommitteeMemberProfile { get; set; }
}
