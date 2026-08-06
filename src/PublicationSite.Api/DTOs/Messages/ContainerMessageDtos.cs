using PublicationSite.Api.Common;

namespace PublicationSite.Api.DTOs.Messages;

/// <summary>
/// Somebody this person may write to about this publication, and why they are on the list.
/// </summary>
/// <param name="Role">What they are here: "Supervisor", "Coordinator", "Head of Department", "Student", or the role they hold. Shown so a student picking from a list knows who they are choosing.</param>
/// <param name="UnreadFromThem">How many of their messages this person has not opened yet.</param>
/// <param name="LastMessageAt">When either of them last wrote, in either direction. Null when they have never written to each other. It is what a screen opens on once nothing is waiting: the conversation somebody came back to is the one they were last having.</param>
public record MessageCounterpartDto(
    Guid UserId,
    string Name,
    string Role,
    int UnreadFromThem,
    DateTime? LastMessageAt = null);

/// <param name="Outgoing">True when the signed-in person wrote it. The two directions read differently, and the caller should not have to compare ids to tell them apart.</param>
public record ContainerMessageDto(
    Guid Id,
    Guid SenderUserId,
    string SenderName,
    Guid RecipientUserId,
    string RecipientName,
    string Body,
    DateTime SentAt,
    bool Outgoing,
    bool ReadByRecipient,
    IReadOnlyList<MessageAttachmentDto> Attachments);

public record MessageAttachmentDto(Guid Id, string FileName, long SizeInBytes);

/// <summary>
/// A message being sent. The files arrive alongside it as multipart form data rather than in here,
/// because a file base64'd into JSON is a third larger and has to be held whole in memory at both
/// ends.
/// </summary>
public record SendContainerMessageRequest(Guid RecipientUserId, string Body);

/// <summary>
/// What the screen needs to draw itself before anybody has written anything: whether this is
/// switched on at all, who may be written to, and what a message may carry.
/// </summary>
/// <param name="Enabled">False when an administrator has switched messaging off. Everything already written is still returned; nothing new is accepted.</param>
public record ContainerMessagingDto(
    bool Enabled,
    IReadOnlyList<MessageCounterpartDto> Counterparts,
    string AllowedExtensions,
    int MaximumLength = SettingKeys.MessageMaximumLength,
    int MaximumAttachments = SettingKeys.MessageMaximumAttachments);

/// <summary>
/// A message as it arrives from a browser: multipart, because the files come with it.
/// </summary>
public class SendContainerMessageForm
{
    public Guid RecipientUserId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Optional, and usually empty. What a message needs to carry is a screenshot or a photograph
    /// of something; the documents a process asks for go where that process asks for them.
    /// </summary>
    public List<IFormFile>? Files { get; set; }
}
