namespace PublicationSite.Api.Services.Interfaces;

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
    void Delete(string relativePath);
}
