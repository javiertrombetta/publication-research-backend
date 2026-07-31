using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class SystemSettingService(
    ApplicationDbContext db,
    ISystemSettingsProvider settings,
    IAuditService auditService,
    IHostEnvironment environment,
    IConfiguration configuration) : ISystemSettingService
{
    /// <summary>
    /// What registration falls back to when nobody has chosen. Development is open so the team
    /// can make accounts freely; anything else is invite-only, because a deployment that hands
    /// out accounts to whoever guesses the email domain is not a deployment anyone intended.
    /// </summary>
    private string EnvironmentRegistrationDefault => environment.IsDevelopment()
        ? SettingKeys.RegistrationModeOpen
        : SettingKeys.RegistrationModeInviteOnly;

    /// <summary>
    /// Whether a Microsoft Entra tenant is actually configured. Not a setting — it is a fact
    /// about the server, and the difference matters: an administrator switching single sign-on
    /// on should be told when nothing would happen.
    /// </summary>
    private bool AzureSsoConfigured => !string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"]);

    public async Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.SystemSettings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .Select(s => new SystemSettingDto(s.Id, s.Key, s.Value, s.Description, s.UpdatedAt))
            .ToListAsync(cancellationToken);

        // The raw listing exists for support and diagnosis, so it must not become the hole
        // through which a secret walks out.
        return rows
            .Select(s => SettingKeys.Secret.Contains(s.Key) ? s with { Value = "********" } : s)
            .ToList();
    }

    // ---------- Committees ----------

    public async Task<CommitteeSettingsDto> GetCommitteeSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetIntAsync(SettingKeys.CommitteeInternalMembers, SettingKeys.DefaultCommitteeInternalMembers, cancellationToken),
            await settings.GetIntAsync(SettingKeys.CommitteeExternalMembers, SettingKeys.DefaultCommitteeExternalMembers, cancellationToken),
            await settings.GetIntAsync(SettingKeys.CommitteeMinApprovals, SettingKeys.DefaultCommitteeMinApprovals, cancellationToken));

    public async Task<CommitteeSettingsDto> UpdateCommitteeSettingsAsync(
        UpdateCommitteeSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (request.InternalMembers < 0 || request.ExternalMembers < 0)
        {
            throw new BusinessRuleException("A committee cannot require a negative number of members.");
        }

        var total = request.InternalMembers + request.ExternalMembers;
        if (total == 0)
        {
            throw new BusinessRuleException("A committee needs at least one member.");
        }

        if (request.MinimumApprovals < 1)
        {
            throw new BusinessRuleException("At least one approval must be required.");
        }

        // Otherwise no paper could ever pass: the rule would demand more approvals than there
        // are people able to give one.
        if (request.MinimumApprovals > total)
        {
            throw new BusinessRuleException(
                $"A committee of {total} cannot be asked for {request.MinimumApprovals} approvals.");
        }

        await SetPendingAsync(SettingKeys.CommitteeInternalMembers, request.InternalMembers, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CommitteeExternalMembers, request.ExternalMembers, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CommitteeMinApprovals, request.MinimumApprovals, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "CommitteeSettingsUpdated",
            $"Committees now require {request.InternalMembers} internal and {request.ExternalMembers} external " +
            $"members, with {request.MinimumApprovals} approvals. Applies to publications opened from now on.",
            cancellationToken);

        return await GetCommitteeSettingsAsync(cancellationToken);
    }

    // ---------- Passwords ----------

    public async Task<PasswordSettingsDto> GetPasswordSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetIntAsync(SettingKeys.PasswordMinimumLength, SettingKeys.DefaultPasswordMinimumLength, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PasswordRequireDigit, SettingKeys.DefaultPasswordRequireDigit, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PasswordRequireUppercase, SettingKeys.DefaultPasswordRequireUppercase, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PasswordRequireLowercase, SettingKeys.DefaultPasswordRequireLowercase, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PasswordRequireSymbol, SettingKeys.DefaultPasswordRequireSymbol, cancellationToken),
            await settings.GetIntAsync(SettingKeys.PasswordExpiryDays, SettingKeys.DefaultPasswordExpiryDays, cancellationToken),
            await settings.GetIntAsync(SettingKeys.LockoutMaxFailedAttempts, SettingKeys.DefaultLockoutMaxFailedAttempts, cancellationToken),
            await settings.GetIntAsync(SettingKeys.LockoutMinutes, SettingKeys.DefaultLockoutMinutes, cancellationToken));

    public async Task<PasswordSettingsDto> UpdatePasswordSettingsAsync(
        UpdatePasswordSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        // Eight is the floor NIST settles on, and this system holds unpublished research and
        // personal data. An administrator can make the rules stricter, not toothless.
        if (request.MinimumLength is < 8 or > 128)
        {
            throw new BusinessRuleException("The minimum password length must be between 8 and 128 characters.");
        }

        if (request.ExpiryDays < 0)
        {
            throw new BusinessRuleException("Password expiry cannot be negative. Use 0 for passwords that never expire.");
        }

        // Rotate people too often and they start writing passwords down; this is the shortest
        // cycle that stays workable.
        if (request.ExpiryDays is > 0 and < 30)
        {
            throw new BusinessRuleException("Password expiry must be at least 30 days, or 0 for no expiry.");
        }

        // No "unlimited" option here, unlike expiry: an account that can be guessed at forever
        // is the one setting an administrator should not be able to switch off by accident.
        if (request.LockoutAttempts is < 3 or > 20)
        {
            throw new BusinessRuleException("Accounts must lock after between 3 and 20 failed attempts.");
        }

        if (request.LockoutMinutes is < 1 or > 1440)
        {
            throw new BusinessRuleException("A lockout must last between 1 minute and 24 hours.");
        }

        await SetPendingAsync(SettingKeys.PasswordMinimumLength, request.MinimumLength, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PasswordRequireDigit, request.RequireDigit, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PasswordRequireUppercase, request.RequireUppercase, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PasswordRequireLowercase, request.RequireLowercase, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PasswordRequireSymbol, request.RequireSymbol, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PasswordExpiryDays, request.ExpiryDays, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.LockoutMaxFailedAttempts, request.LockoutAttempts, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.LockoutMinutes, request.LockoutMinutes, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "PasswordSettingsUpdated",
            $"Passwords now need at least {request.MinimumLength} characters and " +
            (request.ExpiryDays == 0 ? "never expire. " : $"expire after {request.ExpiryDays} days. ") +
            $"Accounts lock for {request.LockoutMinutes} minutes after {request.LockoutAttempts} failed attempts.",
            cancellationToken);

        return await GetPasswordSettingsAsync(cancellationToken);
    }

    // ---------- Notifications ----------

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetBoolAsync(SettingKeys.EmailNotificationsEnabled, SettingKeys.DefaultEmailNotificationsEnabled, cancellationToken),
            await settings.GetStringAsync(SettingKeys.SmtpHost, cancellationToken),
            await settings.GetIntAsync(SettingKeys.SmtpPort, SettingKeys.DefaultSmtpPort, cancellationToken),
            await settings.GetStringAsync(SettingKeys.SmtpUsername, cancellationToken),
            await settings.GetStringAsync(SettingKeys.SmtpPassword, cancellationToken) is not null,
            await settings.GetBoolAsync(SettingKeys.SmtpUseSsl, SettingKeys.DefaultSmtpUseSsl, cancellationToken),
            await settings.GetStringAsync(SettingKeys.SmtpFromAddress, cancellationToken),
            await settings.GetStringAsync(SettingKeys.SmtpFromName, cancellationToken));

    public async Task<NotificationSettingsDto> UpdateNotificationSettingsAsync(
        UpdateNotificationSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        // Turning email on without somewhere to send it would fail quietly for every message,
        // which is worse than leaving it off — so the switch and its prerequisites are checked
        // together.
        if (request.EmailEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.SmtpHost))
            {
                throw new BusinessRuleException("An SMTP server is needed before email notifications can be sent.");
            }

            if (string.IsNullOrWhiteSpace(request.FromAddress))
            {
                throw new BusinessRuleException("A sender address is needed before email notifications can be sent.");
            }
        }

        if (request.SmtpPort is < 1 or > 65535)
        {
            throw new BusinessRuleException("The SMTP port must be between 1 and 65535.");
        }

        await SetPendingAsync(SettingKeys.EmailNotificationsEnabled, request.EmailEnabled, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.SmtpHost, request.SmtpHost ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.SmtpPort, request.SmtpPort, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.SmtpUsername, request.SmtpUsername ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.SmtpUseSsl, request.UseSsl, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.SmtpFromAddress, request.FromAddress ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.SmtpFromName, request.FromName ?? string.Empty, actingAdminId, cancellationToken);

        // Null means "leave the stored password alone" — the administrator cannot read it back,
        // so making them retype it to change the port would be a trap.
        if (request.SmtpPassword is not null)
        {
            await SetPendingAsync(SettingKeys.SmtpPassword, request.SmtpPassword, actingAdminId, cancellationToken);
        }

        await CommitAsync(actingAdminId, "NotificationSettingsUpdated",
            request.EmailEnabled
                ? $"Email notifications enabled via {request.SmtpHost}."
                : "Email notifications disabled. Notifications will appear in the application only.",
            cancellationToken);

        return await GetNotificationSettingsAsync(cancellationToken);
    }

    // ---------- Access ----------

    public async Task<AccessSettingsDto> GetAccessSettingsAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetStringAsync(SettingKeys.RegistrationMode, cancellationToken);

        return new AccessSettingsDto(
            stored ?? EnvironmentRegistrationDefault,
            stored is null,
            await settings.GetBoolAsync(SettingKeys.AzureSsoEnabled, SettingKeys.DefaultAzureSsoEnabled, cancellationToken),
            AzureSsoConfigured,
            await settings.GetIntAsync(SettingKeys.InvitationValidDays, SettingKeys.DefaultInvitationValidDays, cancellationToken),
            await settings.GetIntAsync(SettingKeys.AccessTokenMinutes, SettingKeys.DefaultAccessTokenMinutes, cancellationToken),
            await settings.GetIntAsync(SettingKeys.RefreshTokenDays, SettingKeys.DefaultRefreshTokenDays, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PublicCatalogueEnabled, SettingKeys.DefaultPublicCatalogueEnabled, cancellationToken));
    }

    public async Task<AccessSettingsDto> UpdateAccessSettingsAsync(
        UpdateAccessSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (request.RegistrationMode is not (SettingKeys.RegistrationModeOpen or SettingKeys.RegistrationModeInviteOnly))
        {
            throw new BusinessRuleException("Registration must be either open or by invitation only.");
        }

        // Refused rather than merely discouraged. Open registration in production means anyone
        // who knows the email domain can create an account and see unpublished research; there
        // is no configuration in which that is what someone meant to do.
        if (request.RegistrationMode == SettingKeys.RegistrationModeOpen && !environment.IsDevelopment())
        {
            throw new BusinessRuleException(
                "Open registration is only available in a development environment. " +
                "Invite people instead — you choose their role as you send the invitation.");
        }

        if (request.InvitationValidDays is < 1 or > 90)
        {
            throw new BusinessRuleException("An invitation must stay valid for between 1 and 90 days.");
        }

        // Long-lived access tokens cannot be withdrawn before they expire — a disabled account
        // keeps working until then — so this stays short and the refresh token carries the
        // length of the session.
        if (request.AccessTokenMinutes is < 5 or > 240)
        {
            throw new BusinessRuleException("An access token must last between 5 minutes and 4 hours.");
        }

        if (request.RefreshTokenDays is < 1 or > 90)
        {
            throw new BusinessRuleException("A session must last between 1 and 90 days.");
        }

        await SetPendingAsync(SettingKeys.RegistrationMode, request.RegistrationMode, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.AzureSsoEnabled, request.AzureSsoEnabled, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.InvitationValidDays, request.InvitationValidDays, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.AccessTokenMinutes, request.AccessTokenMinutes, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.RefreshTokenDays, request.RefreshTokenDays, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PublicCatalogueEnabled, request.PublicCatalogueEnabled, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "AccessSettingsUpdated",
            (request.RegistrationMode == SettingKeys.RegistrationModeOpen
                ? "Anyone with an institutional email address can now create their own account. "
                : "Accounts are now created by invitation only. ")
            + (request.PublicCatalogueEnabled
                ? "The public catalogue is the site's landing page."
                : "The public catalogue is switched off; the sign-in page is the landing page."),
            cancellationToken);

        return await GetAccessSettingsAsync(cancellationToken);
    }

    // ---------- Uploads ----------

    public async Task<UploadSettingsDto> GetUploadSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetIntAsync(SettingKeys.MaxUploadMegabytes, SettingKeys.DefaultMaxUploadMegabytes, cancellationToken),
            await settings.GetStringAsync(SettingKeys.AllowedUploadExtensions, cancellationToken)
                ?? SettingKeys.DefaultAllowedUploadExtensions);

    public async Task<UploadSettingsDto> UpdateUploadSettingsAsync(
        UpdateUploadSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        // The ceiling is the server's, not a preference: ASP.NET Core and the reverse proxy in
        // front of it have their own limits, and a figure above them would be accepted here and
        // then rejected mid-upload, which reads to the student as the site being broken.
        if (request.MaxMegabytes is < 1 or > 200)
        {
            throw new BusinessRuleException("The maximum upload size must be between 1 MB and 200 MB.");
        }

        var extensions = NormaliseExtensions(request.AllowedExtensions);
        if (extensions.Length == 0)
        {
            throw new BusinessRuleException("List at least one file type, for example: pdf, docx");
        }

        await SetPendingAsync(SettingKeys.MaxUploadMegabytes, request.MaxMegabytes, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.AllowedUploadExtensions, extensions, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "UploadSettingsUpdated",
            $"Uploads may now be up to {request.MaxMegabytes} MB, and must be one of: {extensions}.",
            cancellationToken);

        return await GetUploadSettingsAsync(cancellationToken);
    }

    /// <summary>
    /// Turns what an administrator types — "pdf, docx" — into the ".pdf,.docx" the file store
    /// matches on. Nobody should have to know the internal spelling to configure this.
    /// </summary>
    private static string NormaliseExtensions(string? raw) =>
        string.Join(',', (raw ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal));

    // ---------- The institution ----------

    public async Task<InstitutionSettingsDto> GetInstitutionSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetStringAsync(SettingKeys.InstitutionName, cancellationToken) ?? SettingKeys.DefaultInstitutionName,
            await settings.GetStringAsync(SettingKeys.StudentEmailDomain, cancellationToken) ?? SettingKeys.DefaultStudentEmailDomain,
            await settings.GetStringAsync(SettingKeys.StaffEmailDomain, cancellationToken) ?? SettingKeys.DefaultStaffEmailDomain,
            await settings.GetStringAsync(SettingKeys.ItSupportEmail, cancellationToken),
            await settings.GetStringAsync(SettingKeys.ResearchEnquiriesEmail, cancellationToken),
            await settings.GetStringAsync(SettingKeys.PrivacyPolicyUrl, cancellationToken),
            await settings.GetStringAsync(SettingKeys.CurrentAcademicCycle, cancellationToken),
            (await settings.GetStringAsync(SettingKeys.RegistrationMode, cancellationToken)
             ?? EnvironmentRegistrationDefault) == SettingKeys.RegistrationModeOpen,
            // Carried on the anonymous response because the landing page has to be decided before
            // anyone has signed in, which is exactly when no other settings are readable.
            await settings.GetBoolAsync(SettingKeys.PublicCatalogueEnabled, SettingKeys.DefaultPublicCatalogueEnabled, cancellationToken));

    public async Task<InstitutionSettingsDto> UpdateInstitutionSettingsAsync(
        UpdateInstitutionSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BusinessRuleException("The institution needs a name.");
        }

        var studentDomain = NormaliseDomain(request.StudentEmailDomain, "student");
        var staffDomain = NormaliseDomain(request.StaffEmailDomain, "staff");

        // Identical domains would make the role a coin toss: the same address would qualify as
        // both a student and a member of staff, and whichever check ran first would win.
        if (string.Equals(studentDomain, staffDomain, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "Students and staff need different email domains — otherwise an address cannot say which someone is.");
        }

        await SetPendingAsync(SettingKeys.InstitutionName, request.Name.Trim(), actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StudentEmailDomain, studentDomain, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StaffEmailDomain, staffDomain, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.ItSupportEmail, request.ItSupportEmail?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.ResearchEnquiriesEmail, request.ResearchEnquiriesEmail?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PrivacyPolicyUrl, request.PrivacyPolicyUrl?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CurrentAcademicCycle, request.CurrentAcademicCycle?.Trim() ?? string.Empty, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "InstitutionSettingsUpdated",
            $"Student addresses end in {studentDomain} and staff addresses in {staffDomain}." +
            (string.IsNullOrWhiteSpace(request.CurrentAcademicCycle)
                ? string.Empty
                : $" The current cycle is {request.CurrentAcademicCycle.Trim()}."),
            cancellationToken);

        return await GetInstitutionSettingsAsync(cancellationToken);
    }

    private static string NormaliseDomain(string? raw, string which)
    {
        var domain = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (domain.Length == 0)
        {
            throw new BusinessRuleException($"Give the {which} email domain, for example @example.ac.nz");
        }

        // Stored with the leading @ so the comparison is a plain suffix match rather than
        // something that has to remember to add one.
        if (!domain.StartsWith('@'))
        {
            domain = "@" + domain;
        }

        if (!domain.Contains('.') || domain.Length < 4)
        {
            throw new BusinessRuleException($"'{raw}' does not look like an email domain.");
        }

        return domain;
    }

    // ---------- Deadlines ----------

    public async Task<DeadlineSettingsDto> GetDeadlineSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetIntAsync(SettingKeys.SupervisorResponseDays, SettingKeys.DefaultSupervisorResponseDays, cancellationToken),
            await settings.GetIntAsync(SettingKeys.EthicsReviewDays, SettingKeys.DefaultEthicsReviewDays, cancellationToken),
            await settings.GetIntAsync(SettingKeys.CommitteeReviewDays, SettingKeys.DefaultCommitteeReviewDays, cancellationToken));

    public async Task<DeadlineSettingsDto> UpdateDeadlineSettingsAsync(
        UpdateDeadlineSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        foreach (var (days, what) in new[]
                 {
                     (request.SupervisorResponseDays, "supervisor response"),
                     (request.EthicsReviewDays, "ethics review"),
                     (request.CommitteeReviewDays, "committee review")
                 })
        {
            if (days is < 0 or > 365)
            {
                throw new BusinessRuleException(
                    $"The {what} deadline must be between 0 and 365 days. Use 0 for no deadline.");
            }
        }

        await SetPendingAsync(SettingKeys.SupervisorResponseDays, request.SupervisorResponseDays, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.EthicsReviewDays, request.EthicsReviewDays, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CommitteeReviewDays, request.CommitteeReviewDays, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "DeadlineSettingsUpdated",
            "Stage deadlines updated. They mark work as overdue; they do not prevent it being done late.",
            cancellationToken);

        return await GetDeadlineSettingsAsync(cancellationToken);
    }

    // ---------- Writing ----------

    /// <summary>
    /// Stages one key. Nothing is written until <see cref="CommitAsync"/> runs, so a group that
    /// fails validation half way through cannot leave the settings in a state the administrator
    /// never asked for.
    /// </summary>
    private async Task SetPendingAsync(string key, object value, Guid actingAdminId, CancellationToken cancellationToken)
    {
        var text = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        // Local first so a group writing several keys does not re-query one it has already
        // loaded; the fallback is awaited rather than blocking, since a group can be eight keys
        // and each one was a thread parked on the database.
        var setting = db.SystemSettings.Local.FirstOrDefault(s => s.Key == key)
                      ?? await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            setting = new SystemSetting { Key = key };
            db.SystemSettings.Add(setting);
        }

        setting.Value = text;
        setting.UpdatedByUserId = actingAdminId;
        setting.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Commits a group, records why, and drops the cache so the next reader sees the change.
    /// The audit entry carries a sentence rather than the raw keys: a year from now, "committees
    /// now require 3 members" is the thing worth being able to read.
    /// </summary>
    private async Task CommitAsync(Guid actingAdminId, string actionType, string summary, CancellationToken cancellationToken)
    {
        await auditService.LogAuditAsync(actingAdminId, actionType, nameof(SystemSetting), null, comments: summary);
        await db.SaveChangesAsync(cancellationToken);
        settings.Invalidate();
    }
}
