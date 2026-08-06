using PublicationSite.Api.DTOs.Support;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Writing to the institution's IT desk.
///
/// Unlike everything else here, the recipient is not a user of this system. IT support is a desk
/// with a mailbox, not a role somebody signs in as, so there is no notification to raise, nothing
/// to mark as read and no reply to wait for inside the site. What this does is take a message and
/// put it in that mailbox, with the sender's address on it so the answer comes back to them.
/// </summary>
public interface ISupportService
{
    /// <summary>
    /// Whether the desk can be written to from inside the site, and its address for when it
    /// cannot. Asked before a screen decides whether to offer a form or a plain mail link.
    /// </summary>
    Task<SupportContactOptionsDto> GetContactOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one. Refuses rather than silently dropping it when there is no address or no mail
    /// server: a form that accepts a message it cannot deliver is worse than no form.
    /// </summary>
    Task SendToItSupportAsync(
        Guid senderUserId,
        string subject,
        string body,
        IReadOnlyList<(Stream Content, string FileName, long Length)> attachments,
        CancellationToken cancellationToken = default);
}
