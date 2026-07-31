namespace PublicationSite.Api.DTOs.Settings;

/// <summary>
/// Committee composition. Changing these affects publications opened afterwards: each
/// Publication Container keeps the figures that were in force when it was created.
/// </summary>
public record CommitteeSettingsDto(int InternalMembers, int ExternalMembers, int MinimumApprovals);

public record UpdateCommitteeSettingsRequest(int InternalMembers, int ExternalMembers, int MinimumApprovals);

/// <summary>
/// What the system will accept as a password, and how long one lasts.
/// <paramref name="ExpiryDays"/> of zero means passwords never expire.
/// </summary>
public record PasswordSettingsDto(
    int MinimumLength,
    bool RequireDigit,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireSymbol,
    int ExpiryDays,
    int LockoutAttempts,
    int LockoutMinutes);

public record UpdatePasswordSettingsRequest(
    int MinimumLength,
    bool RequireDigit,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireSymbol,
    int ExpiryDays,
    int LockoutAttempts,
    int LockoutMinutes);

/// <summary>
/// Where notifications are sent and whether they are emailed at all. The SMTP password is
/// deliberately absent: <paramref name="HasPassword"/> says whether one is stored, and the
/// value itself never leaves the server.
/// </summary>
public record NotificationSettingsDto(
    bool EmailEnabled,
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUsername,
    bool HasPassword,
    bool UseSsl,
    string? FromAddress,
    string? FromName);

/// <summary>
/// <paramref name="SmtpPassword"/> is optional on update: left null the stored one is kept, so
/// saving the other fields does not require retyping a password the administrator cannot read
/// back. An empty string clears it.
/// </summary>
public record UpdateNotificationSettingsRequest(
    bool EmailEnabled,
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUsername,
    string? SmtpPassword,
    bool UseSsl,
    string? FromAddress,
    string? FromName);

/// <summary>One document a student has to supply at the ethics stage.</summary>
public record EthicsDocumentRequirementDto(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool IsInUse);

public record SaveEthicsDocumentRequirementRequest(string Name, string? Description, int SortOrder);

/// <summary>
/// Who is allowed to get an account, and how long a session lasts.
///
/// <paramref name="RegistrationMode"/> is "Open" or "InviteOnly".
/// <paramref name="IsEnvironmentDefault"/> says the mode has never been set and is being taken
/// from the hosting environment — open in development, invite-only in production — which is
/// worth telling an administrator so they know why it reads as it does.
/// <paramref name="AzureSsoConfigured"/> is not a setting: it reports whether a tenant is
/// actually configured on the server, so the screen can say that switching single sign-on on
/// would currently do nothing.
/// </summary>
public record AccessSettingsDto(
    string RegistrationMode,
    bool IsEnvironmentDefault,
    bool AzureSsoEnabled,
    bool AzureSsoConfigured,
    int InvitationValidDays,
    int AccessTokenMinutes,
    int RefreshTokenDays);

public record UpdateAccessSettingsRequest(
    string RegistrationMode,
    bool AzureSsoEnabled,
    int InvitationValidDays,
    int AccessTokenMinutes,
    int RefreshTokenDays);

/// <summary>What may be uploaded. Extensions are accepted with or without their leading dot.</summary>
public record UploadSettingsDto(int MaxMegabytes, string AllowedExtensions);

public record UpdateUploadSettingsRequest(int MaxMegabytes, string AllowedExtensions);

/// <summary>
/// The institution itself: its name, the address suffixes that decide what someone is, where
/// people write for help or to ask for a paper, and the intake currently running.
/// </summary>
/// <param name="SelfRegistrationOpen">Whether anyone may sign themselves up. Read-only here and set under access settings — it rides along on this group because this is the one endpoint a signed-out visitor can call, and the sign-up page needs to know before offering a form the API would reject. It discloses nothing: attempting to register reveals the same thing.</param>
public record InstitutionSettingsDto(
    string Name,
    string StudentEmailDomain,
    string StaffEmailDomain,
    string? ItSupportEmail,
    string? ResearchEnquiriesEmail,
    string? PrivacyPolicyUrl,
    string? CurrentAcademicCycle,
    bool SelfRegistrationOpen = false);

public record UpdateInstitutionSettingsRequest(
    string Name,
    string StudentEmailDomain,
    string StaffEmailDomain,
    string? ItSupportEmail,
    string? ResearchEnquiriesEmail,
    string? PrivacyPolicyUrl,
    string? CurrentAcademicCycle);

/// <summary>
/// How long each stage is expected to take. Zero means nothing is ever reported late for it.
/// These mark work as overdue; they never stop it being done.
/// </summary>
public record DeadlineSettingsDto(
    int SupervisorResponseDays,
    int EthicsReviewDays,
    int CommitteeReviewDays);

public record UpdateDeadlineSettingsRequest(
    int SupervisorResponseDays,
    int EthicsReviewDays,
    int CommitteeReviewDays);
