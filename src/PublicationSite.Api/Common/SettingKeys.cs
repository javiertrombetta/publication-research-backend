namespace PublicationSite.Api.Common;

/// <summary>
/// The canonical names of every administrator-configurable setting, and the value each one falls
/// back to before an administrator has ever touched it.
///
/// Settings live in a single key/value table, which keeps the schema stable as the list grows but
/// gives no type safety on its own, hence this file. Nothing outside <c>ISystemSettingsProvider</c>
/// should spell a key as a literal string: a typo in a reader silently yields the default rather
/// than failing, which is the worst way for a setting to break.
/// </summary>
public static class SettingKeys
{
    // ---------- Evaluation committees ----------

    /// <summary>
    /// How many reviewers and external members an evaluation committee needs. Applies to
    /// publications created from the moment it changes: each Publication Container records the
    /// figures in force on the day it was opened, so a change never moves the goalposts for
    /// research already under way.
    /// </summary>
    public const string CommitteeReviewerMembers = "committee.reviewer-members";
    public const string CommitteeExternalMembers = "committee.external-members";

    /// <summary>How many committee members must approve for a paper to pass.</summary>
    public const string CommitteeMinApprovals = "committee.min-approvals";

    /// <summary>
    /// Which roles an administrator wants drawn on for committees, as a comma-separated list, and
    /// which individual people to leave out of it whatever role they hold.
    ///
    /// The roles here can only ever narrow <see cref="RoleNames.CommitteeEligible"/>, never widen
    /// it: students are excluded because a committee judges their work, and an account still
    /// holding the placeholder role has no job to be asked about yet. Those two are the rule rather
    /// than a preference, so they are not on offer here.
    ///
    /// An empty list of roles means the default, everyone eligible. It is read that way rather than
    /// as "nobody", because a setting that has never been touched should not quietly make it
    /// impossible to form a committee.
    /// </summary>
    public const string CommitteeCandidateRoles = "committee.candidate-roles";
    public const string CommitteeExcludedUserIds = "committee.excluded-user-ids";

    public const int DefaultCommitteeReviewerMembers = 2;
    public const int DefaultCommitteeExternalMembers = 1;
    public const int DefaultCommitteeMinApprovals = 2;

    // ---------- Passwords ----------

    public const string PasswordMinimumLength = "password.minimum-length";
    public const string PasswordRequireDigit = "password.require-digit";
    public const string PasswordRequireUppercase = "password.require-uppercase";
    public const string PasswordRequireLowercase = "password.require-lowercase";
    public const string PasswordRequireSymbol = "password.require-symbol";

    /// <summary>
    /// How many days a password stays valid. Zero means it never expires, which is the sensible
    /// default for an institution that has no help desk to absorb the reset traffic.
    /// </summary>
    public const string PasswordExpiryDays = "password.expiry-days";

    /// <summary>
    /// How many wrong passwords in a row lock an account, and for how long. Counts every path
    /// that checks a password, signing in and changing one alike: an attacker who has borrowed
    /// an unlocked laptop attacks the change-password form, not the sign-in page.
    /// </summary>
    public const string LockoutMaxFailedAttempts = "password.lockout-attempts";
    public const string LockoutMinutes = "password.lockout-minutes";

    public const int DefaultPasswordMinimumLength = 10;
    public const bool DefaultPasswordRequireDigit = true;
    public const bool DefaultPasswordRequireUppercase = true;
    public const bool DefaultPasswordRequireLowercase = true;
    public const bool DefaultPasswordRequireSymbol = true;
    public const int DefaultPasswordExpiryDays = 0;
    public const int DefaultLockoutMaxFailedAttempts = 5;
    public const int DefaultLockoutMinutes = 15;

    // ---------- Notifications and email ----------

    /// <summary>
    /// The master switch. With it off nothing is emailed and every notification is delivered
    /// in the application only, where the person sees it when they next sign in.
    /// </summary>
    public const string EmailNotificationsEnabled = "notifications.email-enabled";

    public const string SmtpHost = "smtp.host";
    public const string SmtpPort = "smtp.port";
    public const string SmtpUsername = "smtp.username";

    /// <summary>
    /// Write-only over the API: it is accepted on save and never returned on read, so an
    /// administrator's browser is never handed the mail account's password.
    /// </summary>
    public const string SmtpPassword = "smtp.password";

    public const string SmtpUseSsl = "smtp.use-ssl";
    public const string SmtpFromAddress = "smtp.from-address";
    public const string SmtpFromName = "smtp.from-name";

    public const bool DefaultEmailNotificationsEnabled = false;
    public const int DefaultSmtpPort = 587;
    public const bool DefaultSmtpUseSsl = true;

    // ---------- Who may get an account ----------

    /// <summary>
    /// How someone comes to have an account: <c>Open</c> lets anyone with an institutional address
    /// sign themselves up, <c>InviteOnly</c> means an administrator invites them.
    ///
    /// The default is not a constant. It comes from the hosting environment. Development wants
    /// self-registration so the team can create accounts freely; a production deployment must not
    /// hand out accounts to anyone who guesses the domain. An administrator can override either
    /// way, but an unconfigured system is never accidentally open in production.
    /// </summary>
    public const string RegistrationMode = "access.registration-mode";

    public const string RegistrationModeOpen = "Open";
    public const string RegistrationModeInviteOnly = "InviteOnly";

    /// <summary>
    /// Whether staff and students are expected to arrive through Microsoft Entra ID rather than
    /// with a password here. The token plumbing already exists and switches on whether an AzureAd
    /// tenant is configured; this says whether the institution intends to use it, which is what the
    /// sign-in page and the invitation rules need to know.
    ///
    /// External committee members are outside the tenant by definition, so they are always invited
    /// and always sign in with a password. No setting changes that.
    /// </summary>
    public const string AzureSsoEnabled = "access.azure-sso-enabled";

    /// <summary>
    /// Whether the public catalogue is served to people without an account.
    ///
    /// On, it is the site's front door and the first thing a visitor sees. Off, there is no public
    /// face at all: the sign-in page becomes the landing page and the catalogue is not reachable,
    /// which is what an institution wants while it is still deciding what to publish, or where
    /// research is not for general release.
    ///
    /// This is enforced here rather than by hiding a link. A catalogue that is merely unlinked is
    /// still readable by anyone who knows the address, which is not what turning it off means.
    /// </summary>
    public const string PublicCatalogueEnabled = "access.public-catalogue";

    public const bool DefaultPublicCatalogueEnabled = true;
    public const bool DefaultItSupportShownToVisitors = false;

    /// <summary>How long an invitation stays usable before it has to be sent again.</summary>
    public const string InvitationValidDays = "access.invitation-valid-days";

    public const bool DefaultAzureSsoEnabled = false;
    public const int DefaultInvitationValidDays = 14;

    // ---------- Sessions ----------

    public const string AccessTokenMinutes = "session.access-token-minutes";
    public const string RefreshTokenDays = "session.refresh-token-days";

    public const int DefaultAccessTokenMinutes = 30;
    public const int DefaultRefreshTokenDays = 14;

    // ---------- Uploads ----------

    public const string MaxUploadMegabytes = "uploads.max-megabytes";

    /// <summary>
    /// Comma-separated, leading dots optional. An administrator should be able to type "pdf, docx"
    /// without knowing the internal format.
    /// </summary>
    public const string AllowedUploadExtensions = "uploads.allowed-extensions";

    public const int DefaultMaxUploadMegabytes = 50;
    public const string DefaultAllowedUploadExtensions = ".pdf,.doc,.docx,.zip";

    // ---------- Writing to each other ----------

    /// <summary>
    /// Whether people may write to each other through a publication at all.
    ///
    /// On unless an institution says otherwise: this is the route a student has to ask their
    /// supervisor a question without leaving the record of their research, and switching it off
    /// sends them back to personal email, where nothing that is said can be found again.
    ///
    /// Off hides the screen and refuses the endpoint. What has already been written stays: a
    /// message somebody relied on is not a thing to delete because a switch moved.
    /// </summary>
    public const string MessagingEnabled = "messaging.enabled";

    /// <summary>
    /// Whether a message written here is noted in the publication's activity history.
    ///
    /// Off unless an institution says otherwise, and the cautious way round rather than the tidy
    /// one. The activity history is read by everybody with access to the publication, so noting a
    /// message there tells a coordinator and a head of department that a student wrote to their
    /// supervisor, which is not theirs unless the institution has decided it is.
    ///
    /// What is noted is who wrote to whom and when, never what was said. Somewhere that treats
    /// this record as the audit trail of a supervision needs the fact; nowhere needs the contents.
    /// </summary>
    public const string MessagingRecordedInActivityHistory = "messaging.record-in-activity-history";

    /// <summary>
    /// What a message may carry, comma-separated, leading dots optional.
    ///
    /// Its own list rather than the document one, because the two are for different things. A
    /// student explaining a problem attaches a screenshot; the documents a process asks for are
    /// uploaded where that process asks for them, and this list is not the place to widen that.
    /// </summary>
    public const string MessagingAllowedExtensions = "messaging.allowed-extensions";

    public const bool DefaultMessagingEnabled = true;
    public const bool DefaultMessagingRecordedInActivityHistory = false;

    /// <summary>
    /// Images, because a screenshot is what most questions come with, plus the everyday document
    /// types. No archives: a zip in a conversation is a way to hand somebody a file the list would
    /// otherwise have refused.
    /// </summary>
    public const string DefaultMessagingAllowedExtensions =
        ".pdf,.doc,.docx,.txt,.png,.jpg,.jpeg,.gif,.webp,.heic";

    /// <summary>
    /// How long one message may be. Long enough for anything anybody needs to explain, short
    /// enough that the column is not a place to paste a dissertation into.
    /// </summary>
    public const int MessageMaximumLength = 4000;

    /// <summary>How many files one message may carry.</summary>
    public const int MessageMaximumAttachments = 5;

    /// <summary>
    /// How many files a message to the IT desk may carry, and how large the lot may be.
    ///
    /// Tighter than a message between two people, and for a different reason. Those are stored,
    /// so their size is whatever the storage rules allow. These are read into memory, attached to
    /// an email and thrown away: nothing keeps them, so nothing may be asked to hold much.
    /// </summary>
    public const int SupportMaximumAttachments = 3;

    public const int SupportMaximumAttachmentMegabytes = 10;

    // ---------- The institution ----------

    public const string InstitutionName = "institution.name";

    /// <summary>
    /// The address suffixes that decide what someone is when they register or arrive from
    /// single sign-on. These were constants in the registration code; an institution that adds a
    /// second student domain should not need a deployment to accept it.
    /// </summary>
    public const string StudentEmailDomain = "institution.student-email-domain";
    public const string StaffEmailDomain = "institution.staff-email-domain";

    /// <summary>
    /// Shown rather than linked when blank, so the interface never offers a dead mailto.
    /// </summary>
    public const string ItSupportEmail = "institution.it-support-email";

    /// <summary>
    /// Whether the IT desk is offered to people with no account: the sign-in page, and the public
    /// catalogue read by anybody. Off unless an institution says otherwise, because a desk that
    /// supports its own students and staff cannot act on a message from a passer-by, and an address
    /// on a page open to the world is an address that will be scraped.
    /// </summary>
    public const string ItSupportShownToVisitors = "institution.it-support-shown-to-visitors";

    /// <summary>
    /// Where the public writes to ask for a paper's full text. Research is not downloadable from
    /// the catalogue, so this address is the whole of that route.
    /// </summary>
    public const string ResearchEnquiriesEmail = "institution.research-enquiries-email";

    public const string PrivacyPolicyUrl = "institution.privacy-policy-url";

    /// <summary>
    /// The institution's own website, where its contact details live.
    ///
    /// Somewhere to send a member of the public who wants the full text of a paper and finds no
    /// enquiries address published. Telling them to get in touch "through the usual channels" is
    /// not an answer to anybody who does not already know what those are, and a telephone number
    /// hardcoded in a view would outlive its accuracy. A URL an administrator maintains does not.
    /// </summary>
    public const string WebsiteUrl = "institution.website-url";

    /// <summary>
    /// How many rows a listing shows before it pages.
    ///
    /// A number the institution chooses rather than one the code decides. Ten suits a queue of
    /// cards on a laptop and wastes a wide screen a coordinator works at all day; the people who
    /// know which of those this is are the ones running the place.
    ///
    /// Applies to every listing, so it is one figure and not one per screen: a reader who has
    /// learned that a page here is twenty rows should not have to relearn it on the next screen.
    /// </summary>
    public const string RowsPerPage = "display.rows-per-page";

    public const int DefaultRowsPerPage = 10;

    /// <summary>
    /// The range an administrator may choose from. Below five a pager appears on almost every
    /// list; above the page ceiling the API would clamp the number and the screen would quietly
    /// disagree with the setting.
    /// </summary>
    public const int MinimumRowsPerPage = 5;
    public const int MaximumRowsPerPage = 100;

    public const string DefaultInstitutionName = "Auckland Institute of Studies";
    public const string DefaultStudentEmailDomain = "@aisstudent.ac.nz";
    public const string DefaultStaffEmailDomain = "@ais.ac.nz";

    // ---------- Deadlines ----------

    /// <summary>
    /// How long each stage is expected to take, in days. Zero means no expectation is set, and
    /// nothing is ever reported late.
    ///
    /// These describe when work becomes overdue; they do not stop anyone doing it afterwards.
    /// A deadline that blocked a supervisor from responding late would only strand the student.
    /// </summary>
    public const string SupervisorResponseDays = "deadlines.supervisor-response-days";
    public const string EthicsReviewDays = "deadlines.ethics-review-days";
    public const string CommitteeReviewDays = "deadlines.committee-review-days";

    public const int DefaultSupervisorResponseDays = 14;
    public const int DefaultEthicsReviewDays = 21;
    public const int DefaultCommitteeReviewDays = 30;

    /// <summary>
    /// How many days before each of those a reminder goes to whoever owes the work.
    ///
    /// A deadline nobody is reminded of is only discovered once it has passed, which is too late
    /// for the person who could have met it. Zero turns the reminder off without touching the
    /// deadline itself, and a lead time longer than the deadline is refused: it would fire the
    /// moment the work arrived.
    /// </summary>
    public const string SupervisorResponseWarningDays = "deadlines.supervisor-response-warning-days";
    public const string EthicsReviewWarningDays = "deadlines.ethics-review-warning-days";
    public const string CommitteeReviewWarningDays = "deadlines.committee-review-warning-days";

    public const int DefaultSupervisorResponseWarningDays = 3;
    public const int DefaultEthicsReviewWarningDays = 3;
    public const int DefaultCommitteeReviewWarningDays = 5;

    // ---------- Research proposals ----------

    /// <summary>
    /// How many research proposals a student submits in one round.
    ///
    /// A round is a set offered together for a supervisor to choose between, so the minimum is
    /// what makes it a choice and the maximum is what keeps it readable. Both apply again when a
    /// coordinator sends a student back to write more: the second round is a round like the first,
    /// and letting it through with one proposal would defeat the reason the first was refused.
    /// </summary>
    public const string ProposalsMinimumPerRound = "proposals.minimum-per-round";
    public const string ProposalsMaximumPerRound = "proposals.maximum-per-round";

    public const int DefaultProposalsMinimumPerRound = 1;
    public const int DefaultProposalsMaximumPerRound = 3;

    /// <summary>A ceiling on the ceiling: a round nobody could read is not a round.</summary>
    public const int HighestProposalsPerRound = 20;

    // ---------- Steps of the ethics pipeline ----------

    /// <summary>
    /// Whether the Head of Department comments on ethics documentation between the coordinator's
    /// approval and their final decision.
    ///
    /// Some institutions want that reading; others have no head of department in the loop and the
    /// step simply parks every publication on a queue nobody works. Turned off, the coordinator's
    /// approval goes straight to their own final decision. Publications already sitting at the
    /// step move on with everything else, which is the point: the setting exists to unstick them.
    /// </summary>
    public const string EthicsHeadOfDepartmentReview = "ethics.head-of-department-review";

    public const bool DefaultEthicsHeadOfDepartmentReview = true;

    /// <summary>
    /// The same reading, on the other route through the stage: where the supervisor ruled that no
    /// ethics documentation is needed and the coordinator agreed.
    ///
    /// Its own setting rather than the one above, because the two are different questions. Reading
    /// documents is work, and an institution may want it done once; agreeing that a piece of
    /// research needs no ethics approval at all is a judgement a head of department may want sight
    /// of precisely because there is nothing to read.
    /// </summary>
    public const string EthicsHeadOfDepartmentReviewNotRequired = "ethics.head-of-department-review-not-required";

    public const bool DefaultEthicsHeadOfDepartmentReviewNotRequired = true;

    /// <summary>
    /// Whether the supervisor reads the uploaded documents, and whether the coordinator does.
    ///
    /// Some institutions have the supervisor check the paperwork and the coordinator only file it;
    /// others the reverse. Either reading can go, but not every one of them: something has to be
    /// read before the stage can be closed, so the settings refuse a sequence with no reader in it.
    /// </summary>
    public const string EthicsSupervisorReviewsDocuments = "ethics.supervisor-reviews-documents";
    public const string EthicsCoordinatorReviewsDocuments = "ethics.coordinator-reviews-documents";

    public const bool DefaultEthicsSupervisorReviewsDocuments = true;
    public const bool DefaultEthicsCoordinatorReviewsDocuments = true;

    /// <summary>
    /// Which of the two reads the documents first. Both readings are the same act on the same
    /// files, so either order is coherent; which one an institution wants depends on whether the
    /// supervisor is checking the work or the coordinator is checking the paperwork.
    /// </summary>
    public const string EthicsDocumentReviewOrder = "ethics.document-review-order";

    public const string SupervisorFirst = "SupervisorFirst";
    public const string CoordinatorFirst = "CoordinatorFirst";
    public const string DefaultEthicsDocumentReviewOrder = SupervisorFirst;

    // ---------- Steps of the research paper stage ----------

    /// <summary>
    /// The three readings a paper can go through: the supervisor's, the evaluation committee's and
    /// the coordinator's decision. Any of them can go, and whichever is last accepts the paper, so
    /// the settings refuse a stage with none of them left: a paper would then be submitted into
    /// nothing.
    /// </summary>
    public const string PaperSupervisorReviews = "paper.supervisor-reviews";
    public const string PaperCommitteeEvaluates = "paper.committee-evaluates";
    public const string PaperCoordinatorDecides = "paper.coordinator-decides";

    public const bool DefaultPaperSupervisorReviews = true;
    public const bool DefaultPaperCommitteeEvaluates = true;
    public const bool DefaultPaperCoordinatorDecides = true;

    /// <summary>
    /// Whether ethics approval comes before the research paper or after it.
    ///
    /// The two are interchangeable: one institution wants ethics cleared before any writing, the
    /// next wants the paper judged first and ethics settled before it is published. Research
    /// proposals stay first either way, because the supervisor who rules on ethics and the
    /// supervisor who reads the paper are both appointed by assigning a proposal.
    /// </summary>
    public const string PipelineEthicsBeforePaper = "pipeline.ethics-before-paper";

    public const bool DefaultPipelineEthicsBeforePaper = true;

    // ---------- Steps of the research proposals stage ----------

    /// <summary>
    /// Whether proposals go out to supervisors, who say which they are willing to take on, before
    /// the coordinator appoints one.
    ///
    /// Off, the coordinator appoints a supervisor to a proposal directly. Some institutions decide
    /// that between themselves and want the round trip; others have the coordinator place students
    /// and do not.
    /// </summary>
    public const string ProposalsSupervisorsExpressInterest = "proposals.supervisors-express-interest";

    public const bool DefaultProposalsSupervisorsExpressInterest = true;

    // ---------- Where uploaded files are kept ----------

    /// <summary>
    /// Which backend new uploads are written to, by name: "local", "database", "s3" or
    /// "azure-blob". Files already stored keep being read from wherever they were written, so
    /// changing this is safe at any time and never has to be timed around anything.
    /// </summary>
    public const string StorageProvider = "storage.provider";

    /// <summary>
    /// The directory the local backend writes under. Absolute, or relative to the application's
    /// content root. A network share is this same backend pointed at a mounted path or a UNC path,
    /// which is why there is no separate provider for one: to the application it is a directory,
    /// and whether it is on this machine is a question for whoever mounted it.
    /// </summary>
    public const string StorageLocalPath = "storage.local.path";

    public const string StorageS3Bucket = "storage.s3.bucket";
    public const string StorageS3Region = "storage.s3.region";

    /// <summary>
    /// Left empty for Amazon's own S3. Set it to reach anything else that speaks S3, which is most
    /// object storage: MinIO, Wasabi, Backblaze, DigitalOcean Spaces.
    /// </summary>
    public const string StorageS3ServiceUrl = "storage.s3.service-url";

    public const string StorageS3AccessKeyId = "storage.s3.access-key-id";

    /// <summary>Write-only over the API, like the mail password.</summary>
    public const string StorageS3SecretKey = "storage.s3.secret-key";

    /// <summary>
    /// Needed by most S3-compatible services that are not Amazon, where the bucket cannot be part
    /// of the hostname. Harmless on Amazon.
    /// </summary>
    public const string StorageS3ForcePathStyle = "storage.s3.force-path-style";

    public const string StorageAzureContainer = "storage.azure.container";

    /// <summary>Write-only over the API: a Blob Storage connection string carries its own key.</summary>
    public const string StorageAzureConnectionString = "storage.azure.connection-string";

    public const string DefaultStorageProvider = "local";
    public const string DefaultStorageLocalPath = "App_Data/uploads";
    public const string DefaultStorageAzureContainer = "uploads";
    public const bool DefaultStorageS3ForcePathStyle = false;

    /// <summary>
    /// Keys whose values must never leave the server. Read endpoints report whether one is set
    /// rather than what it is.
    /// </summary>
    public static readonly IReadOnlySet<string> Secret = new HashSet<string>(StringComparer.Ordinal)
    {
        SmtpPassword,
        StorageS3SecretKey,
        StorageAzureConnectionString
    };
}
