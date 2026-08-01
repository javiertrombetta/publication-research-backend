using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Data.Configurations;

public class StoredFileContentConfiguration : IEntityTypeConfiguration<StoredFileContent>
{
    public void Configure(EntityTypeBuilder<StoredFileContent> builder)
    {
        builder.Property(f => f.SubFolder).HasMaxLength(200).IsRequired();
        builder.Property(f => f.FileName).HasMaxLength(260).IsRequired();

        // longblob: MySQL's default BLOB stops at 64 KB, which would truncate every real document
        // and every photo. The size limit above this decides what actually gets through.
        builder.Property(f => f.Content).HasColumnType("longblob").IsRequired();
    }
}
