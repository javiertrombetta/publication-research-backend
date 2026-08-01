namespace PublicationSite.Api.Services.Interfaces;

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
}
