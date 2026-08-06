using PublicationSite.Api.Common;

namespace PublicationSite.Api.DTOs.Support;

/// <summary>
/// Whether the IT desk can be written to from inside the site, and where to write to it otherwise.
/// </summary>
/// <param name="ThroughTheSite">True when there is an address to send to and a mail server to send through. False means the form would take a message and lose it, so the address below is the honest offer.</param>
/// <param name="EmailAddress">The IT desk's address, for opening a mail client instead. Null when the institution has not set one, in which case there is nothing to offer at all.</param>
public record SupportContactOptionsDto(
    bool ThroughTheSite,
    string? EmailAddress,
    int MaximumLength = SettingKeys.MessageMaximumLength,
    int MaximumAttachments = SettingKeys.SupportMaximumAttachments,
    int MaximumAttachmentMegabytes = SettingKeys.SupportMaximumAttachmentMegabytes);

/// <summary>A message to the IT desk, as it arrives from a browser: multipart, because files come with it.</summary>
public class ContactSupportForm
{
    /// <summary>What it is about, so the desk can triage without opening it.</summary>
    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Usually a screenshot of whatever went wrong, which is the one thing a support desk always
    /// asks for and rarely gets.
    /// </summary>
    public List<IFormFile>? Files { get; set; }
}
