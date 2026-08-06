using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Messages;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// People writing to each other about one publication, through the site rather than around it.
///
/// The point of it being here rather than in personal email is that it stays with the publication.
/// A supervisor picking up a student halfway through can read what was already asked and answered;
/// an exchange in somebody's inbox is gone the moment they are.
/// </summary>
public interface IContainerMessageService
{
    /// <summary>
    /// Whether this is switched on, who this person may write to on this publication, and what a
    /// message may carry. Everything the screen needs before anybody has written anything.
    /// </summary>
    Task<ContainerMessagingDto> GetMessagingAsync(
        Guid publicationContainerId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// This person's correspondence on this publication, newest first. Never anybody else's: a
    /// coordinator with access to the publication does not thereby get to read what the student
    /// wrote to their supervisor.
    /// </summary>
    /// <param name="withUserId">Narrows it to the exchange with one person. Null returns the lot.</param>
    Task<PagedResult<ContainerMessageDto>> GetMessagesAsync(
        Guid publicationContainerId,
        Guid userId,
        Guid? withUserId,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one, with whatever files came with it, and tells the recipient it is there.
    /// </summary>
    Task<ContainerMessageDto> SendAsync(
        Guid publicationContainerId,
        Guid senderUserId,
        SendContainerMessageRequest request,
        IReadOnlyList<(Stream Content, string FileName)> attachments,
        CancellationToken cancellationToken = default);

    /// <summary>Marks as read whatever the other person sent, which is what opening a conversation does.</summary>
    /// <returns>How many were marked.</returns>
    Task<int> MarkReadAsync(
        Guid publicationContainerId, Guid userId, Guid withUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an attachment, if it came with a message this person sent or received. Access to the
    /// publication is not enough on its own, for the same reason the listing is not.
    /// </summary>
    Task<(Stream Content, string FileName)> OpenAttachmentAsync(
        Guid publicationContainerId,
        Guid userId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many messages this person has waiting on this publication. For a badge on the screen
    /// that leads here, so somebody knows to open it.
    /// </summary>
    Task<int> GetUnreadCountAsync(
        Guid publicationContainerId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What an administrator has decided about messaging on this publication in particular, the
    /// people it could be about, and the roles it could be about.
    /// </summary>
    Task<ContainerMessagingRulesDto> GetRulesAsync(
        Guid publicationContainerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets one, replacing whatever was already said about the same target. Silencing somebody
    /// stops them writing and stops anybody writing to them: a one-way rule would leave the other
    /// party sending into nothing.
    /// </summary>
    Task<ContainerMessagingRuleDto> SetRuleAsync(
        Guid publicationContainerId,
        Guid actingAdminId,
        SetContainerMessagingRuleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes one away, so this publication follows the institution's settings again for whoever it
    /// was about.
    /// </summary>
    Task RemoveRuleAsync(
        Guid publicationContainerId, Guid actingAdminId, Guid ruleId, CancellationToken cancellationToken = default);
}
