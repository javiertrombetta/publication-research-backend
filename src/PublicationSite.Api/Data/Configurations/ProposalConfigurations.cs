using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class ResearchProposalConfiguration : IEntityTypeConfiguration<ResearchProposal>
{
    public void Configure(EntityTypeBuilder<ResearchProposal> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(500).IsRequired();
        builder.Property(p => p.Abstract).HasColumnType("text").IsRequired();

        builder.HasOne(p => p.PublicationContainer)
            .WithMany(c => c.Proposals)
            .HasForeignKey(p => p.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProposalSupervisorSelectionConfiguration : IEntityTypeConfiguration<ProposalSupervisorSelection>
{
    public void Configure(EntityTypeBuilder<ProposalSupervisorSelection> builder)
    {
        builder.Property(s => s.Comments).HasColumnType("text");
        builder.HasIndex(s => new { s.ProposalId, s.SupervisorId }).IsUnique();

        builder.HasOne(s => s.Proposal)
            .WithMany(p => p.SupervisorSelections)
            .HasForeignKey(s => s.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Supervisor)
            .WithMany()
            .HasForeignKey(s => s.SupervisorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProposalAssignmentConfiguration : IEntityTypeConfiguration<ProposalAssignment>
{
    public void Configure(EntityTypeBuilder<ProposalAssignment> builder)
    {
        builder.Property(a => a.Comments).HasColumnType("text").IsRequired();
        builder.HasIndex(a => a.ProposalId).IsUnique();

        builder.HasOne(a => a.Proposal)
            .WithOne(p => p.Assignment)
            .HasForeignKey<ProposalAssignment>(a => a.ProposalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Supervisor)
            .WithMany()
            .HasForeignKey(a => a.SupervisorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Coordinator)
            .WithMany()
            .HasForeignKey(a => a.CoordinatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
