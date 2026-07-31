namespace PublicationSite.Api.Entities;

/// <summary>
/// An administrator's offer of an account to a particular address, in a particular role.
///
/// This is how people get in when self-registration is closed, which is every deployment that
/// is not a development one. It also covers the case self-registration never could: external
/// committee members are outside the institution, have no institutional address, and so could
/// never have been given a role by their email domain.
///
/// The role is fixed when the invitation is sent rather than chosen by whoever accepts it —
/// otherwise the invitation would be a way to grant yourself whatever you liked.
/// </summary>
public class UserInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    /// <summary>The role the account will hold. Decided by the administrator, not the invitee.</summary>
    public string Role { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Set for roles that belong to a department. Null for external committee members, who by
    /// definition belong to none.
    /// </summary>
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>
    /// A hash, never the token itself. The token exists only in the email that was sent, so a
    /// leaked database cannot be used to accept anyone's invitation.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public Guid InvitedByUserId { get; set; }
    public ApplicationUser InvitedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the account was created from this invitation.</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// Set when an administrator withdrew it. Kept rather than deleted: who was offered access,
    /// by whom, and who took it back is exactly the kind of thing an audit asks about.
    /// </summary>
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public ApplicationUser? RevokedByUser { get; set; }

    // Deliberately no IsPending here: whether an invitation is pending, accepted, revoked or
    // merely expired is one question with four answers, and it is answered once where the DTO is
    // built. A second copy on the entity would be a second place for it to drift.
}
