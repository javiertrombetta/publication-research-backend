using Microsoft.Extensions.Options;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Stores uploaded files on local disk under FileStorageSettings.RootPath. Implements
/// IFileStorageService so it can be swapped for an Azure Blob Storage implementation
/// later without touching callers, per the client's "Azure Blob Storage (optional)" note.
/// </summary>
public class LocalFileStorageService(
    IOptions<FileStorageSettings> options,
    ISystemSettingsProvider settings,
    IWebHostEnvironment environment)
    : IFileStorageService
{
    private readonly FileStorageSettings _settings = options.Value;

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string subFolder,
        IReadOnlyCollection<string>? allowedExtensions = null,
        CancellationToken cancellationToken = default)
    {
        // An explicit list wins. Profile photos pass their own, and must not be widened by an
        // administrator adding a document type. Otherwise the configured list applies, falling back
        // to appsettings before anyone has configured one.
        var permitted = allowedExtensions ?? await ConfiguredExtensionsAsync(cancellationToken);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!permitted.Contains(extension))
        {
            throw new BusinessRuleException(
                $"'{extension}' files cannot be uploaded. Allowed: {string.Join(", ", permitted)}.");
        }

        var maximumBytes = await MaximumBytesAsync(cancellationToken);

        // Checked up front where the stream can say how long it is, which is every upload that
        // arrives as a form file. The copy below checks again for streams that cannot.
        if (content.CanSeek && content.Length > maximumBytes)
        {
            throw new BusinessRuleException($"Files must be no larger than {maximumBytes / (1024 * 1024)} MB.");
        }

        var rootPath = Path.Combine(environment.ContentRootPath, _settings.RootPath, subFolder);
        Directory.CreateDirectory(rootPath);

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(rootPath, storedFileName);

        try
        {
            await using var fileStream = File.Create(fullPath);
            await CopyWithinLimitAsync(content, fileStream, maximumBytes, cancellationToken);
        }
        catch
        {
            // Nothing useful is left behind by a rejected or failed upload, and a half-written
            // file on disk that no row points at is never cleaned up by anything else.
            File.Delete(fullPath);
            throw;
        }

        var relativePath = Path.Combine(subFolder, storedFileName).Replace('\\', '/');
        return new StoredFile(relativePath, fileName);
    }

    private async Task<IReadOnlyCollection<string>> ConfiguredExtensionsAsync(CancellationToken cancellationToken)
    {
        var raw = await settings.GetStringAsync(SettingKeys.AllowedUploadExtensions, cancellationToken);

        return string.IsNullOrWhiteSpace(raw)
            ? _settings.AllowedExtensions
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<long> MaximumBytesAsync(CancellationToken cancellationToken)
    {
        var megabytes = await settings.GetIntAsync(SettingKeys.MaxUploadMegabytes, 0, cancellationToken);
        return megabytes > 0 ? megabytes * 1024L * 1024L : _settings.MaxFileSizeBytes;
    }

    /// <summary>
    /// Copies while counting, so a stream that could not say its length up front, a chunked upload
    /// for instance, still cannot write more than the limit to disk. Without this the size limit
    /// would be advisory for exactly the uploads most likely to abuse it.
    /// </summary>
    private static async Task CopyWithinLimitAsync(
        Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;
            if (written > maximumBytes)
            {
                throw new BusinessRuleException($"Files must be no larger than {maximumBytes / (1024 * 1024)} MB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
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
