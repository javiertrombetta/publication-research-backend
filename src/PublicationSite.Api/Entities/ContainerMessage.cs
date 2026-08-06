namespace PublicationSite.Api.Entities;

/// <summary>
/// One person writing to another about a publication, through the site.
///
/// Separate from <see cref="ActivityHistoryEntry"/> and deliberately so. The activity history is
/// the record of what was decided and why, written by the workflow and read by everybody with
/// access to the publication. This is a question and an answer between two people, read by those
/// two. Putting them in one place would either expose every exchange to every reader or make the
/// record of the decisions harder to follow, and both are worse than two tables.
///
/// The pairing is fixed at one sender and one recipient rather than a thread with members: every
/// exchange this exists for is between a student and one member of staff, and a group conversation
/// about a student's work that the student is not certain to be part of is not something to build
/// by accident.
/// </summary>
public class ContainerMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public Guid SenderUserId { get; set; }
    public ApplicationUser SenderUser { get; set; } = null!;

    public Guid RecipientUserId { get; set; }
    public ApplicationUser RecipientUser { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the recipient first opened the conversation this sits in. Null until they do. Only the
    /// recipient's reading is recorded: whether the sender re-read their own message is not
    /// information anybody needs.
    /// </summary>
    public DateTime? ReadAt { get; set; }

    public ICollection<ContainerMessageAttachment> Attachments { get; set; } = [];
}

/// <summary>
/// A file sent with a message: a screenshot of what went wrong, a photograph of a signed page, a
/// draft somebody wanted a second opinion on before submitting it.
///
/// Not where the documents a process asks for belong. Those go where that process asks for them,
/// and are reviewed there. The screen says so, because a consent form attached to a message is a
/// consent form nobody reviewing ethics will ever see.
/// </summary>
public class ContainerMessageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContainerMessageId { get; set; }
    public ContainerMessage ContainerMessage { get; set; } = null!;

    /// <summary>What the sender called it, shown and used for the download.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The storage key, which carries the destination that wrote it. See IFileStorageService.</summary>
    public string FilePath { get; set; } = string.Empty;

    public long SizeInBytes { get; set; }
}
