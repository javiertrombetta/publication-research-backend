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

        // Restrict, not cascade: the person named here has commented on the record, and deleting
        // their account must not take the decision with it.
        builder.HasOne(e => e.HeadOfDepartmentUser)
            .WithMany()
            .HasForeignKey(e => e.HeadOfDepartmentUserId)
            .OnDelete(DeleteBehavior.Restrict);
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

        // Restrict: a requirement that has been uploaded against cannot be deleted, only
        // retired. Otherwise removing a form from the list would take submitted work with it.
        builder.HasOne(d => d.EthicsDocumentRequirement)
            .WithMany(r => r.Documents)
            .HasForeignKey(d => d.EthicsDocumentRequirementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class EthicsDocumentRequirementConfiguration : IEntityTypeConfiguration<EthicsDocumentRequirement>
{
    public void Configure(EntityTypeBuilder<EthicsDocumentRequirement> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasColumnType("text");

        // Two forms with the same name would be indistinguishable to the student uploading
        // against them. Retired ones are included: reusing a retired name would make the
        // history ambiguous too.
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public class EthicsApprovalRequirementConfiguration : IEntityTypeConfiguration<EthicsApprovalRequirement>
{
    public void Configure(EntityTypeBuilder<EthicsApprovalRequirement> builder)
    {
        builder.HasIndex(r => new { r.EthicsApprovalId, r.EthicsDocumentRequirementId }).IsUnique();

        builder.HasOne(r => r.EthicsApproval)
            .WithMany(a => a.RequiredDocuments)
            .HasForeignKey(r => r.EthicsApprovalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.EthicsDocumentRequirement)
            .WithMany()
            .HasForeignKey(r => r.EthicsDocumentRequirementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
