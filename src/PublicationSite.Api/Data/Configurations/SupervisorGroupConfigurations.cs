using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class SupervisorGroupConfiguration : IEntityTypeConfiguration<SupervisorGroup>
{
    public void Configure(EntityTypeBuilder<SupervisorGroup> builder)
    {
        builder.Property(g => g.Name).HasMaxLength(80).IsRequired();

        // One name per coordinator. Two groups called the same thing would leave the chooser
        // showing two identical buttons doing different things.
        builder.HasIndex(g => new { g.OwnerId, g.Name }).IsUnique();

        builder.HasOne(g => g.Owner)
            .WithMany()
            .HasForeignKey(g => g.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SupervisorGroupMemberConfiguration : IEntityTypeConfiguration<SupervisorGroupMember>
{
    public void Configure(EntityTypeBuilder<SupervisorGroupMember> builder)
    {
        builder.HasKey(m => new { m.SupervisorGroupId, m.SupervisorId });

        builder.HasOne(m => m.SupervisorGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.SupervisorGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restricted rather than cascading: a group quietly losing a member because an account was
        // removed elsewhere is a change to the coordinator's saved list they never asked for.
        builder.HasOne(m => m.Supervisor)
            .WithMany()
            .HasForeignKey(m => m.SupervisorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
