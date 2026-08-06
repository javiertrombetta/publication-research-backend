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
    public DbSet<DepartmentMembership> DepartmentMemberships => Set<DepartmentMembership>();
    public DbSet<CoordinatorProfile> CoordinatorProfiles => Set<CoordinatorProfile>();
    public DbSet<HeadOfDepartmentProfile> HeadOfDepartmentProfiles => Set<HeadOfDepartmentProfile>();
    public DbSet<CommitteeMemberProfile> CommitteeMemberProfiles => Set<CommitteeMemberProfile>();

    public DbSet<PublicationContainer> PublicationContainers => Set<PublicationContainer>();
    public DbSet<ActivityHistoryEntry> ActivityHistoryEntries => Set<ActivityHistoryEntry>();
    public DbSet<ContainerMessage> ContainerMessages => Set<ContainerMessage>();
    public DbSet<ContainerMessageAttachment> ContainerMessageAttachments => Set<ContainerMessageAttachment>();
    public DbSet<ContainerMessagingRule> ContainerMessagingRules => Set<ContainerMessagingRule>();

    public DbSet<ResearchProposal> ResearchProposals => Set<ResearchProposal>();
    public DbSet<ProposalSupervisorSelection> ProposalSupervisorSelections => Set<ProposalSupervisorSelection>();
    public DbSet<ProposalAssignment> ProposalAssignments => Set<ProposalAssignment>();
    public DbSet<SupervisorGroup> SupervisorGroups => Set<SupervisorGroup>();
    public DbSet<SupervisorGroupMember> SupervisorGroupMembers => Set<SupervisorGroupMember>();

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

    /// <summary>Uploads, for installations that keep their files in the database rather than on a disk or in a bucket.</summary>
    public DbSet<StoredFileContent> StoredFileContents => Set<StoredFileContent>();

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

        // Every row a workflow decision writes carries a stamp, and the stamp is part of the WHERE
        // clause of its UPDATE. Declared here rather than in each configuration file so a new
        // entity gets it by implementing the interface, which is the only thing anyone has to
        // remember. See IHaveAConcurrencyStamp for what it is protecting against.
        foreach (var entity in builder.Model.GetEntityTypes()
                     .Where(e => typeof(IHaveAConcurrencyStamp).IsAssignableFrom(e.ClrType)))
        {
            builder.Entity(entity.ClrType)
                .Property(nameof(IHaveAConcurrencyStamp.ConcurrencyStamp))
                .IsConcurrencyToken();
        }

        // Identity tables keep their defaults but are renamed for a cleaner MySQL schema.
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
    }

    /// <summary>
    /// Moves every stamp on the way out.
    ///
    /// A concurrency token only refuses a second writer if the first one changed it, and EF does
    /// not change it by itself: it compares the value that was read. Done here rather than in each
    /// of the forty-odd places a decision is written, because the one that gets forgotten is the
    /// one that silently stops being protected, and nothing would fail to say so.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Restamp();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        Restamp();
        return base.SaveChanges();
    }

    private void Restamp()
    {
        foreach (var entry in ChangeTracker.Entries<IHaveAConcurrencyStamp>())
        {
            if (entry.State is EntityState.Modified or EntityState.Added)
            {
                entry.Entity.ConcurrencyStamp = Guid.NewGuid();
            }
        }
    }
}
