using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Takes its mail server from the settings an administrator maintains, falling back to
/// appsettings when a field has never been configured. The fallback matters on a fresh
/// deployment: the very first administrator has to receive a verification email before anyone
/// can sign in to configure anything.
/// </summary>
public class SmtpEmailSender(
    ISystemSettingsProvider settings,
    IOptions<MailSettings> mailOptions,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly MailSettings _fallback = mailOptions.Value;

    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var host = await settings.GetStringAsync(SettingKeys.SmtpHost, cancellationToken) ?? _fallback.Host;
        var fromAddress = await settings.GetStringAsync(SettingKeys.SmtpFromAddress, cancellationToken) ?? _fallback.FromAddress;

        // Nothing to connect to. Said once, plainly, rather than as a stack trace from a
        // connection attempt against an empty host name.
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            logger.LogWarning(
                "No mail server is configured, so '{Subject}' was not sent to {Recipient}. " +
                "Set one under System settings.", subject, toEmail);
            return false;
        }

        var port = await settings.GetIntAsync(SettingKeys.SmtpPort, _fallback.Port, cancellationToken);
        var useSsl = await settings.GetBoolAsync(SettingKeys.SmtpUseSsl, _fallback.UseSsl, cancellationToken);
        var username = await settings.GetStringAsync(SettingKeys.SmtpUsername, cancellationToken) ?? _fallback.Username;
        var password = await settings.GetStringAsync(SettingKeys.SmtpPassword, cancellationToken) ?? _fallback.Password;
        var fromName = await settings.GetStringAsync(SettingKeys.SmtpFromName, cancellationToken) ?? _fallback.FromName;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port,
                useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);

            if (!string.IsNullOrWhiteSpace(username))
            {
                await client.AuthenticateAsync(username, password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // Email delivery must never take down a workflow action; the in-app
            // notification (Notification table) remains the source of truth.
            logger.LogError(ex, "Failed to send email to {Recipient} with subject '{Subject}'", toEmail, subject);
            return false;
        }
    }
}
