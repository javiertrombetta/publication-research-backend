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
