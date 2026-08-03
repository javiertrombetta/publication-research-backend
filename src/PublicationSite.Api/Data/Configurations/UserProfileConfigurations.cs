using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.InstitutionalId).HasMaxLength(50);
        builder.Property(u => u.AzureObjectId).HasMaxLength(100);
    }
}

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(d => d.Code).IsUnique();
    }
}

public class ResearchAreaConfiguration : IEntityTypeConfiguration<ResearchArea>
{
    public void Configure(EntityTypeBuilder<ResearchArea> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public class KeywordConfiguration : IEntityTypeConfiguration<Keyword>
{
    public void Configure(EntityTypeBuilder<Keyword> builder)
    {
        builder.Property(k => k.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(k => k.Name).IsUnique();
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.Property(s => s.Key).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Value).HasColumnType("text").IsRequired();
        builder.HasIndex(s => s.Key).IsUnique();

        builder.HasOne(s => s.UpdatedByUser)
            .WithMany()
            .HasForeignKey(s => s.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.Property(s => s.StudentIdNumber).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Programme).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Cohort).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Orcid).HasMaxLength(50);
        builder.HasIndex(s => s.UserId).IsUnique();
        builder.HasIndex(s => s.StudentIdNumber).IsUnique();

        builder.HasOne(s => s.User)
            .WithOne(u => u.StudentProfile)
            .HasForeignKey<StudentProfile>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Department)
            .WithMany(d => d.Students)
            .HasForeignKey(s => s.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PreferredSupervisor)
            .WithMany()
            .HasForeignKey(s => s.PreferredSupervisorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.ResearchAreas)
            .WithMany(r => r.Students)
            .UsingEntity(j => j.ToTable("StudentResearchAreas"));
    }
}

public class SupervisorProfileConfiguration : IEntityTypeConfiguration<SupervisorProfile>
{
    public void Configure(EntityTypeBuilder<SupervisorProfile> builder)
    {
        builder.Property(s => s.AreasOfExpertise).HasColumnType("text");
        builder.Property(s => s.ResearchInterests).HasColumnType("text");
        builder.HasIndex(s => s.UserId).IsUnique();

        builder.HasOne(s => s.User)
            .WithOne(u => u.SupervisorProfile)
            .HasForeignKey<SupervisorProfile>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CoordinatorProfileConfiguration : IEntityTypeConfiguration<CoordinatorProfile>
{
    public void Configure(EntityTypeBuilder<CoordinatorProfile> builder)
    {
        builder.HasIndex(c => c.UserId).IsUnique();

        builder.HasOne(c => c.User)
            .WithOne(u => u.CoordinatorProfile)
            .HasForeignKey<CoordinatorProfile>(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Department)
            .WithMany(d => d.Coordinators)
            .HasForeignKey(c => c.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class HeadOfDepartmentProfileConfiguration : IEntityTypeConfiguration<HeadOfDepartmentProfile>
{
    public void Configure(EntityTypeBuilder<HeadOfDepartmentProfile> builder)
    {
        builder.HasIndex(h => h.UserId).IsUnique();

        // Not unique any more. One head is what a department normally has and what the screens
        // offer, but which people hold it is the administrator's to decide, and a shared or
        // handed-over headship was being refused by the schema rather than by anybody's policy.
        builder.HasIndex(h => h.DepartmentId);

        builder.HasOne(h => h.User)
            .WithOne(u => u.HeadOfDepartmentProfile)
            .HasForeignKey<HeadOfDepartmentProfile>(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Department)
            .WithMany(d => d.HeadsOfDepartment)
            .HasForeignKey(h => h.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CommitteeMemberProfileConfiguration : IEntityTypeConfiguration<CommitteeMemberProfile>
{
    public void Configure(EntityTypeBuilder<CommitteeMemberProfile> builder)
    {
        builder.Property(c => c.Affiliation).HasMaxLength(250);
        builder.HasIndex(c => c.UserId).IsUnique();

        builder.HasOne(c => c.User)
            .WithOne(u => u.CommitteeMemberProfile)
            .HasForeignKey<CommitteeMemberProfile>(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Role).HasMaxLength(100).IsRequired();
        builder.Property(i => i.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(i => i.LastName).HasMaxLength(100).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(200).IsRequired();

        // Accepting an invitation is a lookup by token, and it happens while the caller is
        // anonymous, so it has to be fast and must not degrade into a table scan.
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.Email);

        builder.HasOne(i => i.InvitedByUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.RevokedByUser)
            .WithMany()
            .HasForeignKey(i => i.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Department)
            .WithMany()
            .HasForeignKey(i => i.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


/// <summary>
/// Who belongs to which department, for the roles that can be in more than one.
/// </summary>
public class DepartmentMembershipConfiguration : IEntityTypeConfiguration<DepartmentMembership>
{
    public void Configure(EntityTypeBuilder<DepartmentMembership> builder)
    {
        // The same person in the same department twice is not a second membership, it is the same
        // one saved again, so the database refuses it rather than leaving a screen to notice.
        builder.HasIndex(m => new { m.UserId, m.DepartmentId }).IsUnique();

        builder.HasOne(m => m.User)
            .WithMany(u => u.DepartmentMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restricted, like every other tie to a department: a department somebody still belongs to
        // is not one to delete out from under them.
        builder.HasOne(m => m.Department)
            .WithMany(d => d.Memberships)
            .HasForeignKey(m => m.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
