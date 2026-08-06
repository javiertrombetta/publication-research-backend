namespace PublicationSite.Api.Services.Interfaces;

/// <summary>A file to send with an email. Held in memory, so callers must bound what they read.</summary>
public record EmailAttachment(string FileName, byte[] Content);

public interface IEmailSender
{
    /// <summary>
    /// Sends an email, or reports that it could not. Never throws: email delivery must not be able
    /// to fail a workflow action that has otherwise succeeded.
    /// </summary>
    /// <returns>
    /// True when the message reached the mail server. False when email is not configured, or when
    /// the server refused it. The caller can then avoid recording a delivery that did not happen,
    /// but should not treat it as a failure of the operation itself.
    /// </returns>
    Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends something a person wrote, with the files they attached and their own address to
    /// reply to.
    ///
    /// Separate from <see cref="SendAsync"/>, which sends what the system has to say and is
    /// answered by nobody. This carries somebody's own words out of the site to a mailbox, so a
    /// reply has to reach them rather than the address the site sends from.
    /// </summary>
    Task<bool> ForwardAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string replyToEmail,
        string? replyToName = null,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether there is a mail server to send through at all.
    ///
    /// Its own question because a screen has to decide what to offer before anybody presses
    /// anything: with no server configured, a form that says it will send a message would take one
    /// and lose it, and a plain address for somebody to write to themselves is the honest offer.
    /// </summary>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
}
