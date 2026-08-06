using System.Net;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Support;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class SupportService(
    ApplicationDbContext db,
    IEmailSender emailSender,
    IAuditService auditService,
    ISystemSettingsProvider settings) : ISupportService
{
    public async Task<SupportContactOptionsDto> GetContactOptionsAsync(CancellationToken cancellationToken = default)
    {
        var address = await settings.GetStringAsync(SettingKeys.ItSupportEmail, cancellationToken);
        var hasAddress = !string.IsNullOrWhiteSpace(address);

        return new SupportContactOptionsDto(
            hasAddress && await emailSender.IsConfiguredAsync(cancellationToken),
            hasAddress ? address : null);
    }

    public async Task SendToItSupportAsync(
        Guid senderUserId,
        string subject,
        string body,
        IReadOnlyList<(Stream Content, string FileName, long Length)> attachments,
        CancellationToken cancellationToken = default)
    {
        var options = await GetContactOptionsAsync(cancellationToken);

        if (options.EmailAddress is null)
        {
            throw new BusinessRuleException(
                "No IT support address has been set up, so there is nowhere to send this.");
        }

        if (!options.ThroughTheSite)
        {
            throw new BusinessRuleException(
                $"No mail server is configured, so this cannot be sent from here. Write to {options.EmailAddress} instead.");
        }

        var trimmedSubject = (subject ?? string.Empty).Trim();
        var trimmedBody = (body ?? string.Empty).Trim();

        if (trimmedBody.Length == 0)
        {
            throw new BusinessRuleException("Write something before sending it.");
        }

        if (trimmedBody.Length > SettingKeys.MessageMaximumLength)
        {
            throw new BusinessRuleException(
                $"A message can be up to {SettingKeys.MessageMaximumLength} characters. Yours is {trimmedBody.Length}.");
        }

        if (attachments.Count > SettingKeys.SupportMaximumAttachments)
        {
            throw new BusinessRuleException(
                $"You can attach up to {SettingKeys.SupportMaximumAttachments} files.");
        }

        // Read whole into memory, because an email attachment has to be. Bounded per file rather
        // than trusted: nothing stores these, so nothing has already checked their size, and a
        // request limit on the endpoint governs the envelope rather than any one part of it.
        var maximumBytes = SettingKeys.SupportMaximumAttachmentMegabytes * 1024L * 1024L;
        var carried = new List<EmailAttachment>();

        foreach (var (content, fileName, length) in attachments)
        {
            if (length > maximumBytes)
            {
                throw new BusinessRuleException(
                    $"'{fileName}' is larger than {SettingKeys.SupportMaximumAttachmentMegabytes} MB. "
                    + "Attach a smaller file, or describe what it shows.");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);

            if (buffer.Length > maximumBytes)
            {
                throw new BusinessRuleException(
                    $"'{fileName}' is larger than {SettingKeys.SupportMaximumAttachmentMegabytes} MB.");
            }

            carried.Add(new EmailAttachment(Path.GetFileName(fileName), buffer.ToArray()));
        }

        var sender = await db.Users
            .Where(u => u.Id == senderUserId)
            .Select(u => new { u.FirstName, u.LastName, u.Email })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(ApplicationUser), senderUserId);

        var senderName = $"{sender.FirstName} {sender.LastName}".Trim();
        var institution = await settings.GetStringAsync(SettingKeys.InstitutionName, cancellationToken)
                          ?? SettingKeys.DefaultInstitutionName;

        // Every part of this came from a person typing into a form, so every part of it is encoded
        // before it goes into HTML. A support desk's mail client renders what it is sent, and a
        // message someone wrote is not a place to trust markup.
        var html = $"""
            <p>{WebUtility.HtmlEncode(senderName)} wrote to the IT desk from the {WebUtility.HtmlEncode(institution)} research publication site.</p>
            <p><strong>{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(trimmedSubject) ? "No subject" : trimmedSubject)}</strong></p>
            <p>{WebUtility.HtmlEncode(trimmedBody).Replace("\n", "<br />")}</p>
            <hr />
            <p>Reply to this email to answer {WebUtility.HtmlEncode(senderName)} at {WebUtility.HtmlEncode(sender.Email ?? "an address that is not recorded")}.</p>
            """;

        var sent = await emailSender.ForwardAsync(
            options.EmailAddress,
            string.IsNullOrWhiteSpace(trimmedSubject)
                ? $"Support request from {senderName}"
                : $"Support: {trimmedSubject}",
            html,
            sender.Email ?? string.Empty,
            senderName,
            carried,
            cancellationToken);

        if (!sent)
        {
            // Said out loud rather than swallowed. Everywhere else a failed email is a copy of a
            // notification that is already in the database; here the email is the whole delivery,
            // and somebody who was told it went is owed the truth when it did not.
            throw new BusinessRuleException(
                $"The message could not be sent. Write to {options.EmailAddress} directly, or try again shortly.");
        }

        // Recorded, because it left the site with somebody's name on it. The subject only: what
        // they wrote is between them and the desk.
        await auditService.LogAuditAsync(
            senderUserId,
            "ItSupportContacted",
            "Support",
            null,
            comments: string.IsNullOrWhiteSpace(trimmedSubject)
                ? $"Wrote to the IT desk with {carried.Count} file(s) attached."
                : $"Wrote to the IT desk about '{trimmedSubject}', with {carried.Count} file(s) attached.");
    }
}
