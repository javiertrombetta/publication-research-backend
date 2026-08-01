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

    /// <summary>
    /// Copies a stored file to another destination and returns its new key. The original is left
    /// where it is: this is a copy, so an interrupted run leaves nothing missing and a key that
    /// has not been updated yet still points at something real.
    /// </summary>
    /// <param name="subFolder">Where it should sit at the destination. Taken from the caller rather than from the source key, because a file kept in the database has no folder to read one from.</param>
    Task<string> CopyToAsync(
        string relativePath, string targetProvider, string subFolder, CancellationToken cancellationToken = default);

    /// <summary>Which destination a stored key belongs to, for deciding whether it needs moving at all.</summary>
    string ProviderOf(string relativePath);
}
