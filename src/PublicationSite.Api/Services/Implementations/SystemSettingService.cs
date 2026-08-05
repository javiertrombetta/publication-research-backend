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
    IConfiguration configuration,
    IFileStorageService fileStorage,
    IStorageMigrationService storageMigration) : ISystemSettingService
{
    /// <summary>
    /// Whether the data on this deployment is disposable, which is what decides whether anybody
    /// may sign themselves up.
    ///
    /// The test used to be "is this a development environment", which was wrong in a way that only
    /// showed up once there was a hosted testing deployment: it runs with ASPNETCORE_ENVIRONMENT
    /// set to Production, because it is a real server rather than somebody's laptop, and that made
    /// open registration unreachable on the one deployment where it is harmless and wanted.
    ///
    /// The honest question is not what the environment is called. It is whether there is anything
    /// here worth protecting, and Seed:DemoData already answers it: a deployment carrying the
    /// demonstration dataset holds no real research, and one without it holds either real work or
    /// nothing worth having. That is the same test the database reset endpoint turns on, for the
    /// same reason.
    /// </summary>
    private bool DataIsDisposable => DemoDataSeeder.IsEnabled(configuration, environment);

    /// <summary>
    /// What registration falls back to when nobody has chosen. Open where the data is disposable so
    /// the team can make accounts freely; anything else is invite-only, because a deployment that
    /// hands out accounts to whoever guesses the email domain is not a deployment anyone intended.
    /// </summary>
    private string EnvironmentRegistrationDefault => DataIsDisposable
        ? SettingKeys.RegistrationModeOpen
        : SettingKeys.RegistrationModeInviteOnly;

    /// <summary>
    /// Whether a Microsoft Entra tenant is actually configured. Not a setting. It is a fact about
    /// the server, and the difference matters: an administrator switching single sign-on on should
    /// be told when nothing would happen.
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
            await settings.GetIntAsync(SettingKeys.CommitteeReviewerMembers, SettingKeys.DefaultCommitteeReviewerMembers, cancellationToken),
            await settings.GetIntAsync(SettingKeys.CommitteeExternalMembers, SettingKeys.DefaultCommitteeExternalMembers, cancellationToken),
            await settings.GetIntAsync(SettingKeys.CommitteeMinApprovals, SettingKeys.DefaultCommitteeMinApprovals, cancellationToken),
            await GetCandidateRolesAsync(cancellationToken),
            await GetExcludedCommitteeUsersAsync(cancellationToken),
            RoleNames.CommitteeEligible);

    /// <summary>
    /// The roles an administrator has chosen to draw committees from. Nothing chosen means everyone
    /// eligible, because a setting nobody has touched should not make forming a committee
    /// impossible. Anything no longer eligible is dropped on the way out, so a role removed from
    /// the system cannot linger in a stored list and quietly widen the rule.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCandidateRolesAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetStringAsync(SettingKeys.CommitteeCandidateRoles, cancellationToken) ?? string.Empty;

        var chosen = stored
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(RoleNames.CommitteeEligible.Contains)
            .Distinct()
            .ToList();

        return chosen.Count > 0 ? chosen : RoleNames.CommitteeEligible;
    }

    /// <summary>People an administrator has taken out of consideration, whatever role they hold.</summary>
    public async Task<IReadOnlyList<Guid>> GetExcludedCommitteeUsersAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetStringAsync(SettingKeys.CommitteeExcludedUserIds, cancellationToken) ?? string.Empty;

        return stored
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) ? id : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }

    public async Task<CommitteeSettingsDto> UpdateCommitteeSettingsAsync(
        UpdateCommitteeSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (request.ReviewerMembers < 0 || request.ExternalMembers < 0)
        {
            throw new BusinessRuleException("A committee cannot require a negative number of members.");
        }

        var total = request.ReviewerMembers + request.ExternalMembers;
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

        // Only ever a narrowing of who is eligible. A name that is not on that list is refused
        // rather than ignored, because silently dropping it would leave the screen showing a choice
        // that was never saved.
        var candidateRoles = (request.CandidateRoles ?? [])
            .Select(role => role.Trim())
            .Where(role => role.Length > 0)
            .Distinct()
            .ToList();

        var unknown = candidateRoles.Where(role => !RoleNames.CommitteeEligible.Contains(role)).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException(
                $"These cannot sit on a committee: {string.Join(", ", unknown)}. Students are excluded because a "
                + "committee judges their work, and an account with no role yet has no job to be asked about.");
        }

        if (request.CandidateRoles is not null && candidateRoles.Count == 0)
        {
            throw new BusinessRuleException(
                "Choose at least one role for committees to draw on, or no committee could ever be formed.");
        }

        await SetPendingAsync(SettingKeys.CommitteeReviewerMembers, request.ReviewerMembers, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CommitteeExternalMembers, request.ExternalMembers, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CommitteeMinApprovals, request.MinimumApprovals, actingAdminId, cancellationToken);

        if (request.CandidateRoles is not null)
        {
            await SetPendingAsync(SettingKeys.CommitteeCandidateRoles,
                string.Join(",", candidateRoles), actingAdminId, cancellationToken);
        }

        if (request.ExcludedUserIds is not null)
        {
            await SetPendingAsync(SettingKeys.CommitteeExcludedUserIds,
                string.Join(",", request.ExcludedUserIds.Distinct()), actingAdminId, cancellationToken);
        }

        await CommitAsync(actingAdminId, "CommitteeSettingsUpdated",
            $"Committees now require {request.ReviewerMembers} reviewers and {request.ExternalMembers} external " +
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
        // Turning email on without somewhere to send it would fail quietly for every message, which
        // is worse than leaving it off, so the switch and its prerequisites are checked together.
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

        // Null means "leave the stored password alone". The administrator cannot read it back, so
        // making them retype it to change the port would be a trap.
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
            await settings.GetBoolAsync(SettingKeys.PublicCatalogueEnabled, SettingKeys.DefaultPublicCatalogueEnabled, cancellationToken),
            DataIsDisposable);
    }

    public async Task<AccessSettingsDto> UpdateAccessSettingsAsync(
        UpdateAccessSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (request.RegistrationMode is not (SettingKeys.RegistrationModeOpen or SettingKeys.RegistrationModeInviteOnly))
        {
            throw new BusinessRuleException("Registration must be either open or by invitation only.");
        }

        // Refused rather than merely discouraged. Open registration on a deployment holding real
        // work means anyone who knows the email domain can create an account and read unpublished
        // research; there is no configuration in which that is what somebody meant to do.
        if (request.RegistrationMode == SettingKeys.RegistrationModeOpen && !DataIsDisposable)
        {
            throw new BusinessRuleException(
                "Open registration is only available where the data is disposable, which is what "
                + "seeding the demonstration dataset marks. Invite people instead, and you choose "
                + "their role as you send the invitation.");
        }

        if (request.InvitationValidDays is < 1 or > 90)
        {
            throw new BusinessRuleException("An invitation must stay valid for between 1 and 90 days.");
        }

        // Long-lived access tokens cannot be withdrawn before they expire. A disabled account keeps
        // working until then, so this stays short and the refresh token carries the length of the
        // session.
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
    /// Turns what an administrator types, "pdf, docx", into the ".pdf,.docx" the file store matches
    /// on. Nobody should have to know the internal spelling to configure this.
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
            await settings.GetStringAsync(SettingKeys.WebsiteUrl, cancellationToken),
            (await settings.GetStringAsync(SettingKeys.RegistrationMode, cancellationToken)
             ?? EnvironmentRegistrationDefault) == SettingKeys.RegistrationModeOpen,
            // Carried on the anonymous response because the landing page has to be decided before
            // anyone has signed in, which is exactly when no other settings are readable.
            await settings.GetBoolAsync(SettingKeys.PublicCatalogueEnabled, SettingKeys.DefaultPublicCatalogueEnabled, cancellationToken),
            // Carried here because the site reads this response on every page anyway, and how long
            // a page is has to be known before the first listing is drawn.
            await settings.GetIntAsync(SettingKeys.RowsPerPage, SettingKeys.DefaultRowsPerPage, cancellationToken),
            // Also on the anonymous response, because whether to offer the IT desk has to be
            // decided on the very pages where nobody has signed in.
            await settings.GetBoolAsync(SettingKeys.ItSupportShownToVisitors,
                SettingKeys.DefaultItSupportShownToVisitors, cancellationToken));

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
                "Students and staff need different email domains. Otherwise an address cannot say which someone is.");
        }

        // Refused rather than clamped. A number outside the range is somebody meaning something
        // the system cannot do, and silently saving a different one leaves them believing it did.
        if (request.RowsPerPage < SettingKeys.MinimumRowsPerPage || request.RowsPerPage > SettingKeys.MaximumRowsPerPage)
        {
            throw new BusinessRuleException(
                $"Rows per page has to be between {SettingKeys.MinimumRowsPerPage} and {SettingKeys.MaximumRowsPerPage}.");
        }

        var rowsPerPage = request.RowsPerPage;

        await SetPendingAsync(SettingKeys.InstitutionName, request.Name.Trim(), actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StudentEmailDomain, studentDomain, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StaffEmailDomain, staffDomain, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.ItSupportEmail, request.ItSupportEmail?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.ResearchEnquiriesEmail, request.ResearchEnquiriesEmail?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PrivacyPolicyUrl, request.PrivacyPolicyUrl?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.CurrentAcademicCycle, request.CurrentAcademicCycle?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.WebsiteUrl, request.WebsiteUrl?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.RowsPerPage, rowsPerPage.ToString(), actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.ItSupportShownToVisitors,
            request.ItSupportShownToVisitors.ToString(), actingAdminId, cancellationToken);

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

    // ---------- Comments on decisions ----------

    public async Task<DecisionCommentSettingsDto> GetDecisionCommentSettingsAsync(CancellationToken cancellationToken = default)
    {
        var decisions = new List<DecisionCommentDto>(DecisionPoints.All.Count);

        foreach (var decision in DecisionPoints.All)
        {
            var required = await settings.GetBoolAsync(
                DecisionPoints.SettingKeyFor(decision.Key), decision.RequiredByDefault, cancellationToken);

            decisions.Add(new DecisionCommentDto(
                decision.Key, decision.Stage, decision.Name, required, decision.RequiredByDefault));
        }

        return new DecisionCommentSettingsDto(decisions);
    }

    public async Task<DecisionCommentSettingsDto> UpdateDecisionCommentSettingsAsync(
        UpdateDecisionCommentSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var required = request.Required?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var unknown = required.Where(key => DecisionPoints.Find(key) is null).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"No such decision: {string.Join(", ", unknown)}.");
        }

        // Every decision written every time, including the ones left optional. A screen that only
        // sent the ticked ones could never turn one off again, since an absent key would be
        // indistinguishable from one nobody mentioned.
        foreach (var decision in DecisionPoints.All)
        {
            await SetPendingAsync(
                DecisionPoints.SettingKeyFor(decision.Key), required.Contains(decision.Key), actingAdminId, cancellationToken);
        }

        await CommitAsync(actingAdminId, "DecisionCommentSettingsUpdated",
            $"{required.Count} of {DecisionPoints.All.Count} decisions now require a comment.",
            cancellationToken);

        return await GetDecisionCommentSettingsAsync(cancellationToken);
    }

    // ---------- Steps of the ethics pipeline ----------

    public async Task<EthicsWorkflowSettingsDto> GetEthicsWorkflowSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetBoolAsync(
                SettingKeys.EthicsHeadOfDepartmentReview, SettingKeys.DefaultEthicsHeadOfDepartmentReview, cancellationToken),
            await settings.GetBoolAsync(
                SettingKeys.EthicsHeadOfDepartmentReviewNotRequired,
                SettingKeys.DefaultEthicsHeadOfDepartmentReviewNotRequired, cancellationToken),
            await settings.GetBoolAsync(
                SettingKeys.EthicsSupervisorReviewsDocuments,
                SettingKeys.DefaultEthicsSupervisorReviewsDocuments, cancellationToken),
            await settings.GetBoolAsync(
                SettingKeys.EthicsCoordinatorReviewsDocuments,
                SettingKeys.DefaultEthicsCoordinatorReviewsDocuments, cancellationToken),
            await settings.GetStringAsync(SettingKeys.EthicsDocumentReviewOrder, cancellationToken)
                ?? SettingKeys.DefaultEthicsDocumentReviewOrder);

    public async Task<EthicsWorkflowSettingsDto> UpdateEthicsWorkflowSettingsAsync(
        UpdateEthicsWorkflowSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        await SetPendingAsync(
            SettingKeys.EthicsHeadOfDepartmentReview, request.HeadOfDepartmentReviews, actingAdminId, cancellationToken);

        // Something has to read the documents before the stage closes. A sequence with nobody in
        // it would leave uploads sitting where no screen ever offers them.
        if (!request.SupervisorReviewsDocuments && !request.CoordinatorReviewsDocuments && !request.HeadOfDepartmentReviews)
        {
            throw new BusinessRuleException(
                "Somebody has to read the ethics documents. Leave at least one of the supervisor, "
                + "the coordinator or the Head of Department reading them.");
        }

        await SetPendingAsync(
            SettingKeys.EthicsHeadOfDepartmentReviewNotRequired, request.HeadOfDepartmentReviewsWhenNotRequired,
            actingAdminId, cancellationToken);

        await SetPendingAsync(
            SettingKeys.EthicsSupervisorReviewsDocuments, request.SupervisorReviewsDocuments,
            actingAdminId, cancellationToken);

        await SetPendingAsync(
            SettingKeys.EthicsCoordinatorReviewsDocuments, request.CoordinatorReviewsDocuments,
            actingAdminId, cancellationToken);

        if (request.DocumentReviewOrder is not (SettingKeys.SupervisorFirst or SettingKeys.CoordinatorFirst))
        {
            throw new BusinessRuleException("The documents are read either supervisor first or coordinator first.");
        }

        await SetPendingAsync(
            SettingKeys.EthicsDocumentReviewOrder, request.DocumentReviewOrder, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "EthicsWorkflowSettingsUpdated",
            $"Head of Department reviews ethics documentation: {Word(request.HeadOfDepartmentReviews)}. "
            + $"Reviews where no documentation was needed: {Word(request.HeadOfDepartmentReviewsWhenNotRequired)}. "
            + "Both apply to approvals already waiting at that step, which is the point of the switches.",
            cancellationToken);

        static string Word(bool on) => on ? "yes" : "no";

        return await GetEthicsWorkflowSettingsAsync(cancellationToken);
    }

    // ---------- Steps of the research paper stage ----------

    public async Task<PaperWorkflowSettingsDto> GetPaperWorkflowSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetBoolAsync(SettingKeys.PaperSupervisorReviews, SettingKeys.DefaultPaperSupervisorReviews, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PaperCommitteeEvaluates, SettingKeys.DefaultPaperCommitteeEvaluates, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PaperCoordinatorDecides, SettingKeys.DefaultPaperCoordinatorDecides, cancellationToken),
            await settings.GetBoolAsync(SettingKeys.PipelineEthicsBeforePaper, SettingKeys.DefaultPipelineEthicsBeforePaper, cancellationToken));

    public async Task<PaperWorkflowSettingsDto> UpdatePaperWorkflowSettingsAsync(
        UpdatePaperWorkflowSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        // Whichever reading is last accepts the paper. With none of them left a submitted paper
        // would go into nothing, and no screen would ever offer it to anybody.
        if (!request.SupervisorReviews && !request.CommitteeEvaluates && !request.CoordinatorDecides)
        {
            throw new BusinessRuleException(
                "Somebody has to judge a research paper. Leave at least one of the supervisor, "
                + "the committee or the coordinator on the stage.");
        }

        await SetPendingAsync(SettingKeys.PaperSupervisorReviews, request.SupervisorReviews, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PaperCommitteeEvaluates, request.CommitteeEvaluates, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PaperCoordinatorDecides, request.CoordinatorDecides, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.PipelineEthicsBeforePaper, request.EthicsBeforePaper, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "PaperWorkflowSettingsUpdated",
            $"Supervisor reads a submitted paper: {Word(request.SupervisorReviews)}. "
            + $"An evaluation committee judges it: {Word(request.CommitteeEvaluates)}. "
            + $"The coordinator decides on it: {Word(request.CoordinatorDecides)}. "
            + $"Ethics comes before the research paper: {Word(request.EthicsBeforePaper)}. "
            + "These apply to publications already under way, which is the point of the switches.",
            cancellationToken);

        return await GetPaperWorkflowSettingsAsync(cancellationToken);

        static string Word(bool on) => on ? "yes" : "no";
    }

    // ---------- Research proposals ----------

    public async Task<ProposalSettingsDto> GetProposalSettingsAsync(CancellationToken cancellationToken = default) =>
        new(
            await settings.GetIntAsync(SettingKeys.ProposalsMinimumPerRound, SettingKeys.DefaultProposalsMinimumPerRound, cancellationToken),
            await settings.GetIntAsync(SettingKeys.ProposalsMaximumPerRound, SettingKeys.DefaultProposalsMaximumPerRound, cancellationToken));

    public async Task<ProposalSettingsDto> UpdateProposalSettingsAsync(
        UpdateProposalSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        if (request.MinimumPerRound < 1 || request.MinimumPerRound > SettingKeys.HighestProposalsPerRound)
        {
            throw new BusinessRuleException(
                $"A student must submit at least one proposal, and no more than {SettingKeys.HighestProposalsPerRound}.");
        }

        if (request.MaximumPerRound < request.MinimumPerRound || request.MaximumPerRound > SettingKeys.HighestProposalsPerRound)
        {
            throw new BusinessRuleException(
                $"The most a student may submit has to be at least the fewest, and no more than {SettingKeys.HighestProposalsPerRound}.");
        }

        await SetPendingAsync(SettingKeys.ProposalsMinimumPerRound, request.MinimumPerRound, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.ProposalsMaximumPerRound, request.MaximumPerRound, actingAdminId, cancellationToken);

        await CommitAsync(actingAdminId, "ProposalSettingsUpdated",
            $"A round of research proposals is now {request.MinimumPerRound} to {request.MaximumPerRound}. "
            + "It applies to rounds asked for again as well as to first ones.",
            cancellationToken);

        return await GetProposalSettingsAsync(cancellationToken);
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

    // ---------- Where uploaded files are kept ----------

    /// <summary>The destinations this build has, in the order the settings screen offers them.</summary>
    private static readonly string[] KnownProviders = ["local", "database", "s3", "azure-blob"];

    public async Task<StorageSettingsDto> GetStorageSettingsAsync(CancellationToken cancellationToken = default)
    {
        var localPath = await settings.GetStringAsync(SettingKeys.StorageLocalPath, cancellationToken);

        return new StorageSettingsDto(
            await settings.GetStringAsync(SettingKeys.StorageProvider, cancellationToken)
                is { Length: > 0 } provider ? provider : SettingKeys.DefaultStorageProvider,
            string.IsNullOrWhiteSpace(localPath) ? SettingKeys.DefaultStorageLocalPath : localPath,
            await settings.GetStringAsync(SettingKeys.StorageS3Bucket, cancellationToken),
            await settings.GetStringAsync(SettingKeys.StorageS3Region, cancellationToken),
            await settings.GetStringAsync(SettingKeys.StorageS3ServiceUrl, cancellationToken),
            await settings.GetStringAsync(SettingKeys.StorageS3AccessKeyId, cancellationToken),
            // Whether it is set, never what it is.
            await settings.GetStringAsync(SettingKeys.StorageS3SecretKey, cancellationToken) is not null,
            await settings.GetBoolAsync(SettingKeys.StorageS3ForcePathStyle,
                SettingKeys.DefaultStorageS3ForcePathStyle, cancellationToken),
            await settings.GetStringAsync(SettingKeys.StorageAzureContainer, cancellationToken)
                is { Length: > 0 } container ? container : SettingKeys.DefaultStorageAzureContainer,
            await settings.GetStringAsync(SettingKeys.StorageAzureConnectionString, cancellationToken) is not null,
            // So the screen can offer the copy only when there is something to copy, and say how
            // much rather than asking the administrator to guess.
            await storageMigration.CountElsewhereAsync(cancellationToken));
    }

    public async Task<StorageSettingsDto> UpdateStorageSettingsAsync(
        UpdateStorageSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var provider = (request.Provider ?? string.Empty).Trim().ToLowerInvariant();

        if (!KnownProviders.Contains(provider))
        {
            throw new BusinessRuleException(
                $"'{request.Provider}' is not a storage destination. Choose one of: {string.Join(", ", KnownProviders)}.");
        }

        // Checked before it is saved rather than after. Saving a destination that cannot be
        // reached would break the next upload, and the administrator would have no way to tell
        // whether it was the setting or the bucket.
        await EnsureDestinationIsUsableAsync(provider, request, cancellationToken);

        await SetPendingAsync(SettingKeys.StorageProvider, provider, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StorageLocalPath,
            string.IsNullOrWhiteSpace(request.LocalPath) ? SettingKeys.DefaultStorageLocalPath : request.LocalPath.Trim(),
            actingAdminId, cancellationToken);

        await SetPendingAsync(SettingKeys.StorageS3Bucket, request.S3Bucket?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StorageS3Region, request.S3Region?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StorageS3ServiceUrl, request.S3ServiceUrl?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StorageS3AccessKeyId, request.S3AccessKeyId?.Trim() ?? string.Empty, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StorageS3ForcePathStyle, request.S3ForcePathStyle, actingAdminId, cancellationToken);
        await SetPendingAsync(SettingKeys.StorageAzureContainer,
            string.IsNullOrWhiteSpace(request.AzureContainer) ? SettingKeys.DefaultStorageAzureContainer : request.AzureContainer.Trim(),
            actingAdminId, cancellationToken);

        // Null means "leave the stored secret alone". The administrator cannot read it back, so
        // making them retype it to change a bucket name would be a trap.
        if (request.S3SecretKey is not null)
        {
            await SetPendingAsync(SettingKeys.StorageS3SecretKey, request.S3SecretKey, actingAdminId, cancellationToken);
        }

        if (request.AzureConnectionString is not null)
        {
            await SetPendingAsync(SettingKeys.StorageAzureConnectionString, request.AzureConnectionString, actingAdminId, cancellationToken);
        }

        await CommitAsync(actingAdminId, "StorageSettingsUpdated",
            $"New uploads will be stored in: {Describe(provider)}. Files already stored are unaffected.",
            cancellationToken);

        return await GetStorageSettingsAsync(cancellationToken);
    }

    public async Task<StorageCheckResultDto> CheckStorageAsync(
        string? provider = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await fileStorage.CheckAsync(provider, cancellationToken);
            return new StorageCheckResultDto(true, $"{Describe(provider)} answered and accepted a write.");
        }
        catch (Exception ex)
        {
            // Reported rather than thrown: the administrator asked a question, and "no, because"
            // is the answer to it. A 500 here would look like the settings screen was broken.
            return new StorageCheckResultDto(false, ex.Message);
        }
    }

    /// <summary>
    /// Writes the settings into the pending change set and asks the destination whether it works,
    /// then puts them back. The backends read their configuration through the same provider
    /// everything else does, so this is the only way to test a destination that has not been saved.
    /// </summary>
    private async Task EnsureDestinationIsUsableAsync(
        string provider, UpdateStorageSettingsRequest request, CancellationToken cancellationToken)
    {
        if (provider == "local" && string.IsNullOrWhiteSpace(request.LocalPath))
        {
            throw new BusinessRuleException("Say which directory files should be written to.");
        }

        if (provider == "s3" && string.IsNullOrWhiteSpace(request.S3Bucket))
        {
            throw new BusinessRuleException("Say which S3 bucket files should be written to.");
        }

        if (provider == "azure-blob"
            && request.AzureConnectionString is null
            && await settings.GetStringAsync(SettingKeys.StorageAzureConnectionString, cancellationToken) is null)
        {
            throw new BusinessRuleException("Azure Blob Storage needs a connection string.");
        }
    }

    private static string Describe(string? provider) => provider switch
    {
        "database" => "the database",
        "s3" => "S3",
        "azure-blob" => "Azure Blob Storage",
        "local" => "a directory on the server",
        _ => "the configured destination"
    };

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
