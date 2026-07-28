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

public class PublicationCategoryConfiguration : IEntityTypeConfiguration<PublicationCategory>
{
    public void Configure(EntityTypeBuilder<PublicationCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
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

        builder.HasOne(s => s.Department)
            .WithMany(d => d.Supervisors)
            .HasForeignKey(s => s.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
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
        builder.HasIndex(h => h.DepartmentId).IsUnique();

        builder.HasOne(h => h.User)
            .WithOne(u => u.HeadOfDepartmentProfile)
            .HasForeignKey<HeadOfDepartmentProfile>(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Department)
            .WithOne(d => d.HeadOfDepartment)
            .HasForeignKey<HeadOfDepartmentProfile>(h => h.DepartmentId)
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
