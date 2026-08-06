using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class ContainerMessageConfiguration : IEntityTypeConfiguration<ContainerMessage>
{
    public void Configure(EntityTypeBuilder<ContainerMessage> builder)
    {
        builder.Property(m => m.Body).HasColumnType("text").IsRequired();

        builder.HasOne(m => m.PublicationContainer)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restricted, like every other reference to a person here: an account is retired rather
        // than removed, and a conversation with the sender's name missing from it is not a
        // conversation anybody can make sense of afterwards.
        builder.HasOne(m => m.SenderUser)
            .WithMany()
            .HasForeignKey(m => m.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.RecipientUser)
            .WithMany()
            .HasForeignKey(m => m.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every read of this table is "the messages on this publication involving me, newest
        // first", so that is the index. Without it a conversation costs a scan of everybody's.
        builder.HasIndex(m => new { m.PublicationContainerId, m.SentAt });
        builder.HasIndex(m => m.RecipientUserId);
    }
}

public class ContainerMessageAttachmentConfiguration : IEntityTypeConfiguration<ContainerMessageAttachment>
{
    public void Configure(EntityTypeBuilder<ContainerMessageAttachment> builder)
    {
        builder.Property(a => a.FileName).HasMaxLength(300).IsRequired();
        builder.Property(a => a.FilePath).HasMaxLength(1000).IsRequired();

        builder.HasOne(a => a.ContainerMessage)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.ContainerMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ContainerMessagingRuleConfiguration : IEntityTypeConfiguration<ContainerMessagingRule>
{
    public void Configure(EntityTypeBuilder<ContainerMessagingRule> builder)
    {
        builder.Property(r => r.TargetRole).HasMaxLength(64);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();

        builder.HasOne(r => r.PublicationContainer)
            .WithMany(c => c.MessagingRules)
            .HasForeignKey(r => r.PublicationContainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.TargetUser)
            .WithMany()
            .HasForeignKey(r => r.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.SetByUser)
            .WithMany()
            .HasForeignKey(r => r.SetByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Read whole, per publication, every time somebody opens the messages on it. There are
        // never many, so this is the only index worth having.
        //
        // Not unique: MySQL counts NULLs as distinct in a unique index, so a constraint over the
        // two nullable targets would let a second rule for the same role through and refuse
        // nothing worth refusing. One rule per target is kept by the service, which looks for an
        // existing one and updates it.
        builder.HasIndex(r => r.PublicationContainerId);
    }
}
