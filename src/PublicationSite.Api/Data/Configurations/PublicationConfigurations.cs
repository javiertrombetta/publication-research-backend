using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class PublicationConfiguration : IEntityTypeConfiguration<Publication>
{
    public void Configure(EntityTypeBuilder<Publication> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Abstract).HasColumnType("text").IsRequired();
        builder.Property(p => p.PublicationType).HasMaxLength(100);
        builder.HasIndex(p => p.PublicationContainerId).IsUnique();

        builder.HasOne(p => p.PublicationContainer)
            .WithOne(c => c.Publication)
            .HasForeignKey<Publication>(p => p.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.PublishedByUser)
            .WithMany()
            .HasForeignKey(p => p.PublishedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Keywords)
            .WithMany(k => k.Publications)
            .UsingEntity(j => j.ToTable("PublicationKeywords"));

        builder.HasMany(p => p.ResearchAreas)
            .WithMany(r => r.Publications)
            .UsingEntity(j => j.ToTable("PublicationResearchAreas"));
    }
}

public class PublicationVersionConfiguration : IEntityTypeConfiguration<PublicationVersion>
{
    public void Configure(EntityTypeBuilder<PublicationVersion> builder)
    {
        builder.Property(v => v.FilePath).HasMaxLength(1000).IsRequired();
        builder.Property(v => v.SupplementaryFilesPath).HasMaxLength(1000);
        builder.Property(v => v.ReviewerNotes).HasColumnType("text");
        builder.HasIndex(v => new { v.PublicationId, v.VersionNumber }).IsUnique();

        builder.HasOne(v => v.Publication)
            .WithMany(p => p.Versions)
            .HasForeignKey(v => v.PublicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.UploadedByUser)
            .WithMany()
            .HasForeignKey(v => v.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.Property(r => r.Comments).HasColumnType("text").IsRequired();

        builder.HasOne(r => r.PublicationVersion)
            .WithMany(v => v.Reviews)
            .HasForeignKey(r => r.PublicationVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.ReviewerUser)
            .WithMany()
            .HasForeignKey(r => r.ReviewerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CommitteeConfiguration : IEntityTypeConfiguration<Committee>
{
    public void Configure(EntityTypeBuilder<Committee> builder)
    {
        builder.HasIndex(c => c.PublicationId).IsUnique();

        builder.HasOne(c => c.Publication)
            .WithOne(p => p.Committee)
            .HasForeignKey<Committee>(c => c.PublicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


public class CommitteeMemberConfiguration : IEntityTypeConfiguration<CommitteeMember>
{
    public void Configure(EntityTypeBuilder<CommitteeMember> builder)
    {
        builder.Property(m => m.DecisionComments).HasColumnType("text");
        builder.HasIndex(m => new { m.CommitteeId, m.UserId }).IsUnique();

        builder.HasOne(m => m.Committee)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.CommitteeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
