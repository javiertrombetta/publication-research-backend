using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class PublicationContainerConfiguration : IEntityTypeConfiguration<PublicationContainer>
{
    public void Configure(EntityTypeBuilder<PublicationContainer> builder)
    {
        builder.HasOne(c => c.Student)
            .WithMany()
            .HasForeignKey(c => c.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Coordinator)
            .WithMany()
            .HasForeignKey(c => c.CoordinatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AssignedSupervisor)
            .WithMany()
            .HasForeignKey(c => c.AssignedSupervisorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ActivityHistoryEntryConfiguration : IEntityTypeConfiguration<ActivityHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ActivityHistoryEntry> builder)
    {
        builder.Property(a => a.Action).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Comments).HasColumnType("text").IsRequired();
        builder.Property(a => a.PreviousStatus).HasMaxLength(100);
        builder.Property(a => a.NewStatus).HasMaxLength(100);

        builder.HasOne(a => a.PublicationContainer)
            .WithMany(c => c.ActivityHistory)
            .HasForeignKey(a => a.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.OnBehalfOfUser)
            .WithMany()
            .HasForeignKey(a => a.OnBehalfOfUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
