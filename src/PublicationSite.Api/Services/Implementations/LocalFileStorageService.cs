using Microsoft.Extensions.Options;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Stores uploaded files on local disk under FileStorageSettings.RootPath. Implements
/// IFileStorageService so it can be swapped for an Azure Blob Storage implementation
/// later without touching callers, per the client's "Azure Blob Storage (optional)" note.
/// </summary>
public class LocalFileStorageService(IOptions<FileStorageSettings> options, IWebHostEnvironment environment)
    : IFileStorageService
{
    private readonly FileStorageSettings _settings = options.Value;

    public async Task<StoredFile> SaveAsync(Stream content, string fileName, string subFolder, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_settings.AllowedExtensions.Contains(extension))
        {
            throw new BusinessRuleException($"File extension '{extension}' is not allowed.");
        }

        var rootPath = Path.Combine(environment.ContentRootPath, _settings.RootPath, subFolder);
        Directory.CreateDirectory(rootPath);

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(rootPath, storedFileName);

        await using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var relativePath = Path.Combine(subFolder, storedFileName).Replace('\\', '/');
        return new StoredFile(relativePath, fileName);
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(environment.ContentRootPath, _settings.RootPath, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new NotFoundException("File", relativePath);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public void Delete(string relativePath)
    {
        var fullPath = Path.Combine(environment.ContentRootPath, _settings.RootPath, relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
