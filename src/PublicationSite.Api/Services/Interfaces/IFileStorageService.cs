namespace PublicationSite.Api.Services.Interfaces;

public record StoredFile(string RelativePath, string FileName);

public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string subFolder, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);
    void Delete(string relativePath);
}
