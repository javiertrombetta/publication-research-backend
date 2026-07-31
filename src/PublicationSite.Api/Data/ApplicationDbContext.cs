using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ResearchArea> ResearchAreas => Set<ResearchArea>();
    public DbSet<Keyword> Keywords => Set<Keyword>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<EthicsDocumentRequirement> EthicsDocumentRequirements => Set<EthicsDocumentRequirement>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<EthicsApprovalRequirement> EthicsApprovalRequirements => Set<EthicsApprovalRequirement>();

    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<SupervisorProfile> SupervisorProfiles => Set<SupervisorProfile>();
    public DbSet<CoordinatorProfile> CoordinatorProfiles => Set<CoordinatorProfile>();
    public DbSet<HeadOfDepartmentProfile> HeadOfDepartmentProfiles => Set<HeadOfDepartmentProfile>();
    public DbSet<CommitteeMemberProfile> CommitteeMemberProfiles => Set<CommitteeMemberProfile>();

    public DbSet<PublicationContainer> PublicationContainers => Set<PublicationContainer>();
    public DbSet<ActivityHistoryEntry> ActivityHistoryEntries => Set<ActivityHistoryEntry>();

    public DbSet<ResearchProposal> ResearchProposals => Set<ResearchProposal>();
    public DbSet<ProposalSupervisorSelection> ProposalSupervisorSelections => Set<ProposalSupervisorSelection>();
    public DbSet<ProposalAssignment> ProposalAssignments => Set<ProposalAssignment>();

    public DbSet<EthicsDeclaration> EthicsDeclarations => Set<EthicsDeclaration>();
    public DbSet<EthicsApproval> EthicsApprovals => Set<EthicsApproval>();
    public DbSet<EthicsDocument> EthicsDocuments => Set<EthicsDocument>();

    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<PublicationVersion> PublicationVersions => Set<PublicationVersion>();
    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<Committee> Committees => Set<Committee>();
    public DbSet<CommitteeRoleConfig> CommitteeRoleConfigs => Set<CommitteeRoleConfig>();
    public DbSet<CommitteeMember> CommitteeMembers => Set<CommitteeMember>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // MySQL's DATETIME defaults to whole-second precision, silently truncating .NET's
        // sub-second DateTime values on insert. EF Core's default optimistic-concurrency
        // check then compares the full-precision in-memory "original value" against the
        // truncated stored value, matches 0 rows, and every UPDATE throws
        // DbUpdateConcurrencyException. datetime(6) keeps microsecond precision so what's
        // read back always matches what was written.
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime(6)");
        configurationBuilder.Properties<DateTime?>().HaveColumnType("datetime(6)");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Identity tables keep their defaults but are renamed for a cleaner MySQL schema.
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
    }
}
