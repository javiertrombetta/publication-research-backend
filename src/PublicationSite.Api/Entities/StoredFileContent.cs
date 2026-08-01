namespace PublicationSite.Api.Entities;

/// <summary>
/// An uploaded file kept in the database rather than on a disk or in a bucket.
///
/// Worth having for an installation with nowhere sensible to put a directory: everything lives in
/// one place, the backup that covers the data covers the documents, and there is no share to mount
/// or key to rotate. The cost is that the database grows with every upload and every download
/// reads through it, so it suits a department rather than an archive.
/// </summary>
public class StoredFileContent
{
    /// <summary>Also the path this backend hands back, so a row is found by its key alone.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Kept for the audit trail: which upload this was, in the words of the screen that made it.</summary>
    public string SubFolder { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public long Length { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
