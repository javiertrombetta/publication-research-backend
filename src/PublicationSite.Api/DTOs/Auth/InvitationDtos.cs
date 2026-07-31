namespace PublicationSite.Api.DTOs.Auth;

/// <summary>
/// An invitation as an administrator sees it. The token is absent by design — it exists only in
/// the email that was sent, so an administrator reading this list cannot accept on someone's
/// behalf.
/// </summary>
/// <param name="Status"><summary>Pending, Accepted, Revoked or Expired — what the administrator actually reads.</summary></param>
public record UserInvitationDto(
    Guid Id,
    string Email,
    string Role,
    string FirstName,
    string LastName,
    Guid? DepartmentId,
    string? DepartmentName,
    string InvitedByName,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt,
    string Status);

/// <summary>
/// <paramref name="Role"/> is chosen by the administrator as they send it, which is what lets an
/// invitation reach someone with no institutional address — an external committee member has no
/// email domain to be judged by.
/// </summary>
public record CreateInvitationRequest(
    string Email,
    string Role,
    string FirstName,
    string LastName,
    Guid? DepartmentId);

/// <summary>
/// What an invited person is shown before they accept: enough to know the invitation is real
/// and meant for them, and nothing that would be worth guessing a token to obtain.
/// </summary>
public record InvitationPreviewDto(
    string Email,
    string Role,
    string FirstName,
    string LastName,
    string InstitutionName,
    DateTime ExpiresAt);

public record AcceptInvitationRequest(string Token, string Password);
