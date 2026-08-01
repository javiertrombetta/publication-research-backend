using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations.Storage;

/// <summary>
/// Files in a directory.
///
/// This is also the network-share option. A share is a directory once something has mounted it, so
/// pointing the path at /mnt/research or at \\server\research is the whole of the difference, and
/// a second implementation would only have duplicated this one to say so.
/// </summary>
public class LocalFileStorageBackend(
    ISystemSettingsProvider settings,
    IWebHostEnvironment environment) : IFileStorageBackend
{
    public string Name => "local";

    public async Task<string> WriteAsync(
        Stream content, string subFolder, string storedFileName, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(await RootAsync(cancellationToken), subFolder);
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, storedFileName);

        try
        {
            await using var file = File.Create(fullPath);
            await content.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            // A half-written file that no row points at is never cleaned up by anything else.
            File.Delete(fullPath);
            throw;
        }

        return Path.Combine(subFolder, storedFileName).Replace('\\', '/');
    }

    public async Task<Stream> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(await RootAsync(cancellationToken), path);

        if (!File.Exists(fullPath)) throw new NotFoundException("File", path);

        return File.OpenRead(fullPath);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(await RootAsync(cancellationToken), path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var root = await RootAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(root);

            // Written and removed rather than merely looked at: a directory that exists and cannot
            // be written to fails at the first upload, which is the wrong moment to find out.
            var probe = Path.Combine(root, $".write-check-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new BusinessRuleException($"Could not write to '{root}'. {ex.Message}");
        }
    }

    /// <summary>
    /// Read on every call rather than cached, because an administrator can change it and the next
    /// upload has to land in the new place. The provider behind it caches, so this is not a query.
    /// </summary>
    private async Task<string> RootAsync(CancellationToken cancellationToken)
    {
        var configured = await settings.GetStringAsync(SettingKeys.StorageLocalPath, cancellationToken);
        var path = string.IsNullOrWhiteSpace(configured) ? SettingKeys.DefaultStorageLocalPath : configured.Trim();

        // An absolute path is taken as given, which is what a mounted share will be. A relative one
        // is resolved against the application, so the default keeps working with no configuration.
        return Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);
    }
}
