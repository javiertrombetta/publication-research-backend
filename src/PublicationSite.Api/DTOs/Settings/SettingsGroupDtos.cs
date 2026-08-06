using PublicationSite.Api.Common;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.DTOs.Settings;

/// <summary>
/// Committee composition. Changing these affects publications opened afterwards: each
/// Publication Container keeps the figures that were in force when it was created.
/// </summary>
/// <summary>
/// How a committee is composed, and who it may be composed of.
/// <paramref name="CandidateRoles"/> is the administrator's choice of which roles to draw on, and
/// <paramref name="ExcludedUserIds"/> the people to leave out whatever role they hold.
/// <paramref name="SelectableRoles"/> is not a setting: it is every role that could be chosen, so
/// the screen offers the real list rather than one written out again in a view.
/// </summary>
public record CommitteeSettingsDto(
    int ReviewerMembers,
    int ExternalMembers,
    int MinimumApprovals,
    IReadOnlyList<string> CandidateRoles,
    IReadOnlyList<Guid> ExcludedUserIds,
    IReadOnlyList<string> SelectableRoles);

public record UpdateCommitteeSettingsRequest(
    int ReviewerMembers,
    int ExternalMembers,
    int MinimumApprovals,
    IReadOnlyList<string>? CandidateRoles = null,
    IReadOnlyList<Guid>? ExcludedUserIds = null);

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
/// Whether people may write to each other through a publication, and what happens when they do.
/// </summary>
/// <param name="Enabled">Off hides the screen and refuses the endpoint. What was already written stays readable.</param>
/// <param name="RecordedInActivityHistory">Whether the publication's activity history notes that a message was sent. It never records what was said.</param>
/// <param name="AllowedExtensions">What a message may carry, with or without leading dots. Separate from the document list, since a question usually comes with a screenshot and a process document belongs where that process asks for it.</param>
/// <param name="StudentsMayWrite">Whether a student may start a conversation at all.</param>
/// <param name="StudentMayWriteToRoles">Which of the people on their publication they may start it with.</param>
/// <param name="StaffMayWrite">Whether the people working on a publication may write to the student.</param>
/// <param name="StaffMayWriteToStudentRoles">Which of them may.</param>
/// <param name="SelectableStudentRoles">Not settings: every role that could go on the student's list, so a screen offers the real set rather than one written out again in a view. Admin is absent because no administrator is attached to a publication.</param>
/// <param name="SelectableStaffRoles">The same for the staff list.</param>
public record MessagingSettingsDto(
    bool Enabled,
    bool RecordedInActivityHistory,
    string AllowedExtensions,
    bool StudentsMayWrite,
    IReadOnlyList<string> StudentMayWriteToRoles,
    bool StaffMayWrite,
    IReadOnlyList<string> StaffMayWriteToStudentRoles,
    IReadOnlyList<string> SelectableStudentRoles,
    IReadOnlyList<string> SelectableStaffRoles,
    int MaximumLength = SettingKeys.MessageMaximumLength,
    int MaximumAttachments = SettingKeys.MessageMaximumAttachments);

public record UpdateMessagingSettingsRequest(
    bool Enabled,
    bool RecordedInActivityHistory,
    string AllowedExtensions,
    bool StudentsMayWrite = true,
    IReadOnlyList<string>? StudentMayWriteToRoles = null,
    bool StaffMayWrite = true,
    IReadOnlyList<string>? StaffMayWriteToStudentRoles = null);

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
/// <param name="ItSupportReachableThroughTheSite">Whether there is both an address to write to and a mail server to send through, so the footer can offer a form rather than a mail link. Read-only, and derived rather than set: it is two other settings agreeing. Also here because the footer is on every page and this response is already fetched and cached for it; asking a second endpoint per page load to colour one link would not be worth the round trip.</param>
public record InstitutionSettingsDto(
    string Name,
    string StudentEmailDomain,
    string StaffEmailDomain,
    string? ItSupportEmail,
    string? ResearchEnquiriesEmail,
    string? PrivacyPolicyUrl,
    string? WebsiteUrl = null,
    bool SelfRegistrationOpen = false,
    bool PublicCatalogueEnabled = true,
    int RowsPerPage = SettingKeys.DefaultRowsPerPage,
    bool ItSupportShownToVisitors = SettingKeys.DefaultItSupportShownToVisitors,
    bool ItSupportReachableThroughTheSite = false);

public record UpdateInstitutionSettingsRequest(
    string Name,
    string StudentEmailDomain,
    string StaffEmailDomain,
    string? ItSupportEmail,
    string? ResearchEnquiriesEmail,
    string? PrivacyPolicyUrl,
    string? WebsiteUrl = null,
    int RowsPerPage = SettingKeys.DefaultRowsPerPage,
    bool ItSupportShownToVisitors = SettingKeys.DefaultItSupportShownToVisitors);

/// <summary>
/// How long each stage is expected to take. Zero means nothing is ever reported late for it.
/// These mark work as overdue; they never stop it being done.
/// </summary>
/// <param name="SupervisorResponseWarningDays">Days before the supervisor's answer-by date that a reminder goes out. Zero sends none.</param>
/// <param name="EthicsReviewWarningDays">The same for the ethics review.</param>
/// <param name="CommitteeReviewWarningDays">The same for a committee's review.</param>
public record DeadlineSettingsDto(
    int SupervisorResponseDays,
    int EthicsReviewDays,
    int CommitteeReviewDays,
    int SupervisorResponseWarningDays = SettingKeys.DefaultSupervisorResponseWarningDays,
    int EthicsReviewWarningDays = SettingKeys.DefaultEthicsReviewWarningDays,
    int CommitteeReviewWarningDays = SettingKeys.DefaultCommitteeReviewWarningDays);

public record UpdateDeadlineSettingsRequest(
    int SupervisorResponseDays,
    int EthicsReviewDays,
    int CommitteeReviewDays,
    int SupervisorResponseWarningDays = SettingKeys.DefaultSupervisorResponseWarningDays,
    int EthicsReviewWarningDays = SettingKeys.DefaultEthicsReviewWarningDays,
    int CommitteeReviewWarningDays = SettingKeys.DefaultCommitteeReviewWarningDays);

/// <summary>
/// How many research proposals a student submits in one round, and so how many a supervisor is
/// given to choose between. Applies to a round a coordinator has asked for again as well as to
/// the first.
/// </summary>
/// <param name="SupervisorsExpressInterest">Whether proposals go out to supervisors, who say which they are willing to take on, before the coordinator appoints one.</param>
public record ProposalSettingsDto(
    int MinimumPerRound,
    int MaximumPerRound,
    bool SupervisorsExpressInterest = true);

public record UpdateProposalSettingsRequest(
    int MinimumPerRound,
    int MaximumPerRound,
    bool SupervisorsExpressInterest = true);

/// <summary>
/// One decision in the pipeline, and whether this institution asks for a comment on it.
/// </summary>
/// <param name="Key">The stable name of the decision. See Common/DecisionPoints.</param>
/// <param name="Stage">Which part of the pipeline it belongs to, so the screen can group them.</param>
/// <param name="Name">What the decision is, in the words the screen uses.</param>
/// <param name="CommentRequired">What it is set to now.</param>
/// <param name="RequiredByDefault">What it would be if nobody had ever set it, so the screen can say which have been changed.</param>
public record DecisionCommentDto(
    string Key, string Stage, string Name, bool CommentRequired, bool RequiredByDefault);

public record DecisionCommentSettingsDto(IReadOnlyList<DecisionCommentDto> Decisions);

/// <param name="Required">
/// The keys of the decisions that must carry a comment. Anything in DecisionPoints and absent
/// here is optional: the screen posts the whole set every time, so a decision left out is a
/// decision unticked rather than one nobody mentioned.
/// </param>
public record UpdateDecisionCommentSettingsRequest(IReadOnlyList<string> Required);

/// <summary>
/// Which of the research paper's readings this institution runs, and where ethics sits.
/// </summary>
/// <param name="SupervisorReviews">Whether the supervisor reads a submitted paper.</param>
/// <param name="CommitteeEvaluates">Whether an evaluation committee judges it.</param>
/// <param name="CoordinatorDecides">Whether the coordinator makes the final decision on it.</param>
/// <param name="EthicsBeforePaper">Whether ethics approval comes before the research paper or after it.</param>
public record PaperWorkflowSettingsDto(
    bool SupervisorReviews = true,
    bool CommitteeEvaluates = true,
    bool CoordinatorDecides = true,
    bool EthicsBeforePaper = true)
{
    /// <summary>The stage a publication opens on once a proposal has been assigned.</summary>
    public PipelineStage FirstOfTheTwo => EthicsBeforePaper ? PipelineStage.EthicsApproval : PipelineStage.ResearchPaper;

    /// <summary>The stage that follows it.</summary>
    public PipelineStage SecondOfTheTwo => EthicsBeforePaper ? PipelineStage.ResearchPaper : PipelineStage.EthicsApproval;
}

public record UpdatePaperWorkflowSettingsRequest(
    bool SupervisorReviews = true,
    bool CommitteeEvaluates = true,
    bool CoordinatorDecides = true,
    bool EthicsBeforePaper = true);

/// <param name="SupervisorReviewsDocuments">Whether the supervisor reads the uploaded documents before anybody else.</param>
/// <param name="CoordinatorReviewsDocuments">Whether the coordinator reads them before handing on.</param>
public record EthicsWorkflowSettingsDto(
    bool HeadOfDepartmentReviews,
    bool HeadOfDepartmentReviewsWhenNotRequired,
    bool SupervisorReviewsDocuments = true,
    bool CoordinatorReviewsDocuments = true,
    string DocumentReviewOrder = SettingKeys.SupervisorFirst)
{
    /// <summary>Whether the coordinator is the one who reads first.</summary>
    public bool CoordinatorReadsFirst =>
        string.Equals(DocumentReviewOrder, SettingKeys.CoordinatorFirst, StringComparison.OrdinalIgnoreCase);
}

public record UpdateEthicsWorkflowSettingsRequest(
    bool HeadOfDepartmentReviews,
    bool HeadOfDepartmentReviewsWhenNotRequired,
    bool SupervisorReviewsDocuments = true,
    bool CoordinatorReviewsDocuments = true,
    string DocumentReviewOrder = SettingKeys.SupervisorFirst)
{
    /// <summary>Whether the coordinator is the one who reads first.</summary>
    public bool CoordinatorReadsFirst =>
        string.Equals(DocumentReviewOrder, SettingKeys.CoordinatorFirst, StringComparison.OrdinalIgnoreCase);
}
