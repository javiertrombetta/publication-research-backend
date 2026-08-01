namespace PublicationSite.Api.Services.Interfaces;

/// <param name="RelativePath">
/// The key to give back to read this file later. It carries the name of the backend that wrote it,
/// so a file stays readable after an administrator points new uploads somewhere else. Keys written
/// before storage was configurable have no prefix and are local files.
/// </param>
public record StoredFile(string RelativePath, string FileName);

public interface IFileStorageService
{
    /// <param name="allowedExtensions">
    /// Overrides the configured document extensions for this call, used by profile photos, which
    /// accept images that must stay disallowed for document uploads.
    /// </param>
    Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string subFolder,
        IReadOnlyCollection<string>? allowedExtensions = null,
        CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the file from wherever it was written. Asynchronous because a bucket is a network
    /// call: it was synchronous while a local disk was the only possibility.
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proves the configured destination works, so an administrator finds out on the settings
    /// screen rather than when a student uploads.
    /// </summary>
    /// <param name="providerName">A destination to test instead of the one in force, used to check a new one before switching to it.</param>
    Task CheckAsync(string? providerName = null, CancellationToken cancellationToken = default);
}
