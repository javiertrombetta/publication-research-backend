using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class EthicsDeclarationConfiguration : IEntityTypeConfiguration<EthicsDeclaration>
{
    public void Configure(EntityTypeBuilder<EthicsDeclaration> builder)
    {
        builder.HasIndex(e => e.PublicationContainerId).IsUnique();

        builder.HasOne(e => e.PublicationContainer)
            .WithOne(c => c.EthicsDeclaration)
            .HasForeignKey<EthicsDeclaration>(e => e.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EthicsApprovalConfiguration : IEntityTypeConfiguration<EthicsApproval>
{
    public void Configure(EntityTypeBuilder<EthicsApproval> builder)
    {
        builder.Property(e => e.ReferenceNumber).HasMaxLength(100);
        builder.Property(e => e.SupervisorDecisionComments).HasColumnType("text");
        builder.Property(e => e.CoordinatorDecisionComments).HasColumnType("text");
        builder.Property(e => e.HeadOfDepartmentComments).HasColumnType("text");
        builder.HasIndex(e => e.PublicationContainerId).IsUnique();

        builder.HasOne(e => e.PublicationContainer)
            .WithOne(c => c.EthicsApproval)
            .HasForeignKey<EthicsApproval>(e => e.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EthicsDocumentConfiguration : IEntityTypeConfiguration<EthicsDocument>
{
    public void Configure(EntityTypeBuilder<EthicsDocument> builder)
    {
        builder.Property(d => d.FileName).HasMaxLength(300).IsRequired();
        builder.Property(d => d.FilePath).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.ReviewComments).HasColumnType("text");

        builder.HasOne(d => d.EthicsApproval)
            .WithMany(e => e.Documents)
            .HasForeignKey(d => d.EthicsApprovalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.UploadedByUser)
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
