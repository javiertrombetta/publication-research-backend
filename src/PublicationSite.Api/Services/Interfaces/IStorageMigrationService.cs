using PublicationSite.Api.DTOs.Settings;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Brings files stored elsewhere to the destination in force. Optional: nothing depends on it,
/// because a file records where it was written and keeps opening from there.
/// </summary>
public interface IStorageMigrationService
{
    /// <summary>
    /// Copies a batch, repoints the records, and leaves the originals. Run it again to continue:
    /// a file already at the destination is skipped rather than copied twice.
    /// </summary>
    Task<StorageMigrationResultDto> MigrateToActiveAsync(Guid actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>How many stored files are somewhere other than the destination in force.</summary>
    Task<int> CountElsewhereAsync(CancellationToken cancellationToken = default);
}
