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
/// <paramref name="IsEnvironmentDefault"/> says the mode has never been set and is being taken from
/// the hosting environment, open in development and invite-only in production, which is worth
/// telling an administrator so they know why it reads as it does.
/// <paramref name="AzureSsoConfigured"/> is not a setting: it reports whether a tenant is actually
/// configured on the server, so the screen can say that switching single sign-on on would currently
/// do nothing.
/// <paramref name="CanOpenRegistration"/> is not a setting either: it says whether this deployment
/// will accept open registration at all. Stated by the API rather than worked out by whoever is
/// drawing the screen, because the rule turns on the API's own configuration, and a client guessing
/// at it from its own environment gets it wrong the moment the two are deployed separately.
/// </summary>
public record AccessSettingsDto(
    string RegistrationMode,
    bool IsEnvironmentDefault,
    bool AzureSsoEnabled,
    bool AzureSsoConfigured,
    int InvitationValidDays,
    int AccessTokenMinutes,
    int RefreshTokenDays,
    bool PublicCatalogueEnabled = true,
    bool CanOpenRegistration = false);

public record UpdateAccessSettingsRequest(
    string RegistrationMode,
    bool AzureSsoEnabled,
    int InvitationValidDays,
    int AccessTokenMinutes,
    int RefreshTokenDays,
    bool PublicCatalogueEnabled = true);

/// <summary>What may be uploaded. Extensions are accepted with or without their leading dot.</summary>
public record UploadSettingsDto(int MaxMegabytes, string AllowedExtensions);

public record UpdateUploadSettingsRequest(int MaxMegabytes, string AllowedExtensions);

/// <summary>
/// Where uploaded files are kept: ethics documents, research paper versions and profile photos.
///
/// Changing it points new uploads somewhere else and nothing more. Every stored file records the
/// destination that wrote it, so what is already there keeps opening from where it is and there is
/// no migration to run or window to time.
/// </summary>
/// <param name="Provider">"local", "database", "s3" or "azure-blob".</param>
/// <param name="LocalPath">The directory the local option writes under, absolute or relative to the application. A network share is this pointed at a mounted path or a UNC path, which is the whole of the difference.</param>
/// <param name="S3ServiceUrl">Empty for Amazon's own S3. Set for anything else that speaks S3, which is most object storage.</param>
/// <param name="S3SecretKeySet">Whether a secret key is stored. The key itself is never returned.</param>
/// <param name="AzureConnectionStringSet">Whether a connection string is stored. It is never returned, because it carries its own key.</param>
public record StorageSettingsDto(
    string Provider,
    string LocalPath,
    string? S3Bucket,
    string? S3Region,
    string? S3ServiceUrl,
    string? S3AccessKeyId,
    bool S3SecretKeySet,
    bool S3ForcePathStyle,
    string AzureContainer,
    bool AzureConnectionStringSet,
    int FilesElsewhere = 0);

/// <summary>
/// The secrets are optional on update: left null the stored one is kept, so an administrator can
/// change a bucket name without being made to retype a key they are not allowed to read back.
/// </summary>
public record UpdateStorageSettingsRequest(
    string Provider,
    string? LocalPath,
    string? S3Bucket,
    string? S3Region,
    string? S3ServiceUrl,
    string? S3AccessKeyId,
    string? S3SecretKey,
    bool S3ForcePathStyle,
    string? AzureContainer,
    string? AzureConnectionString);

/// <summary>What testing a destination found. Never throws at the caller: a failure is the answer.</summary>
public record StorageCheckResultDto(bool Reachable, string Message);

/// <summary>
/// What one run of the copy did.
/// </summary>
/// <param name="Moved">Files copied to the destination in force, with their records repointed.</param>
/// <param name="Remaining">Still elsewhere. Runs are bounded so a request answers, so more than zero here means run it again.</param>
/// <param name="Problems">The files that could not be copied, and why. One unreadable file does not stop the rest.</param>
public record StorageMigrationResultDto(int Moved, int Remaining, IReadOnlyList<string> Problems);

/// <summary>
/// The institution itself: its name, the address suffixes that decide what someone is, where people
/// write for help or to ask for a paper, and the intake currently running.
/// </summary>
/// <param name="SelfRegistrationOpen">Whether anyone may sign themselves up. Read-only here and set under access settings. It rides along on this group because this is the one endpoint a signed-out visitor can call, and the sign-up page needs to know before offering a form the API would reject. It discloses nothing: attempting to register reveals the same thing.</param>
public record InstitutionSettingsDto(
    string Name,
    string StudentEmailDomain,
    string StaffEmailDomain,
    string? ItSupportEmail,
    string? ResearchEnquiriesEmail,
    string? PrivacyPolicyUrl,
    string? CurrentAcademicCycle,
    string? WebsiteUrl = null,
    bool SelfRegistrationOpen = false,
    bool PublicCatalogueEnabled = true);

public record UpdateInstitutionSettingsRequest(
    string Name,
    string StudentEmailDomain,
    string StaffEmailDomain,
    string? ItSupportEmail,
    string? ResearchEnquiriesEmail,
    string? PrivacyPolicyUrl,
    string? CurrentAcademicCycle,
    string? WebsiteUrl = null);

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
