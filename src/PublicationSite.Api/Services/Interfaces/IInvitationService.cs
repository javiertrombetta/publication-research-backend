using PublicationSite.Api.DTOs.Auth;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Invitations: how someone gets an account when self-registration is closed, which is every
/// deployment that is not a development one.
///
/// It is also the only route that ever existed for external committee members. They are outside
/// the institution, so they have no institutional address, so no email domain could tell the
/// system what they are — an administrator has to say.
/// </summary>
public interface IInvitationService
{
    Task<IReadOnlyList<UserInvitationDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<UserInvitationDto> CreateAsync(
        CreateInvitationRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the invitation again with a fresh token and a new expiry. The previous token stops
    /// working, so a forwarded or intercepted copy of the old email cannot still be used.
    /// </summary>
    Task<UserInvitationDto> ResendAsync(Guid id, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<UserInvitationDto> RevokeAsync(Guid id, Guid actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the invited person is shown before they accept. Anonymous — they have no account yet.
    /// </summary>
    Task<InvitationPreviewDto> PreviewAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the account from the invitation and marks it used. The role comes from the
    /// invitation, never from the request: otherwise accepting an invitation would be a way to
    /// grant yourself whatever role you fancied.
    /// </summary>
    Task AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default);
}
