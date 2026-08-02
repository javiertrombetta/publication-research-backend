using Microsoft.AspNetCore.Identity;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? InstitutionalId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Pending;

    /// <summary>
    /// Whether this person is currently taking work on. Theirs to set, and separate from
    /// <see cref="Status"/>, which is the administrator's.
    ///
    /// They answer different questions. Disabled means the account may not be used at all, and is
    /// imposed. Unavailable means the person is here and working but should not be offered
    /// anything new: on leave, at capacity, between terms. Folding the two together would mean a
    /// supervisor going away for a month either keeps receiving proposals or loses their account.
    ///
    /// It lives on the user, not on a profile, because it is a fact about the person rather than
    /// about a role they happen to hold: somebody who supervises and sits on committees is away
    /// from both at once. Students have one too and nothing reads it, since no decision in the
    /// system chooses a student.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Light or dark, as this person last chose it. Null until they choose, which is what lets the
    /// site follow the machine's own preference for somebody who has never said.
    ///
    /// On the account rather than only in a cookie, because it is a fact about the person: signing
    /// in on a different machine should not hand them back a theme they had already rejected. The
    /// cookie is still what the page is drawn from, since it is readable before anything is
    /// fetched, and this is what refills it at sign-in.
    /// </summary>
    public string? ThemePreference { get; set; }

    public AuthProvider AuthProvider { get; set; } = AuthProvider.Local;
    public string? AzureObjectId { get; set; }

    /// <summary>Storage-relative path of the user's profile photo; null when they have none.</summary>
    public string? ProfilePhotoPath { get; set; }
    /// <summary>
    /// When the password was last set. Null for accounts that predate password expiry, and for
    /// accounts that have never had a local password (Azure sign-in); both are treated as
    /// not expired, so turning expiry on does not lock out the whole institution at once.
    /// </summary>
    public DateTime? PasswordChangedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public StudentProfile? StudentProfile { get; set; }
    public SupervisorProfile? SupervisorProfile { get; set; }
    public CoordinatorProfile? CoordinatorProfile { get; set; }
    public HeadOfDepartmentProfile? HeadOfDepartmentProfile { get; set; }
    public CommitteeMemberProfile? CommitteeMemberProfile { get; set; }
}
