using Microsoft.Extensions.Options;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations.Storage;

/// <summary>
/// The one thing the rest of the application talks to about files.
///
/// It does two jobs the backends deliberately do not. First, it decides what may be uploaded at
/// all: the extension and the size limit are properties of the institution rather than of the
/// place the bytes land, and repeating them per backend is how they end up disagreeing.
///
/// Second, it remembers where each file went. Every stored key carries the name of the backend
/// that wrote it, so an administrator can move new uploads to a bucket on a Tuesday afternoon and
/// everything written before Tuesday still opens, from the disk it is still sitting on. Without
/// that, changing the setting would quietly break every download in the system, and the only way
/// back would be to change it again.
///
/// Keys written before any of this existed have no prefix. Those are local files, because local
/// was the only thing there was.
/// </summary>
public class FileStorageService(
    IEnumerable<IFileStorageBackend> backends,
    ISystemSettingsProvider settings,
    IOptions<FileStorageSettings> options) : IFileStorageService
{
    private const char Separator = ':';
    private readonly FileStorageSettings _fallback = options.Value;

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

        // Counted into memory first, so a stream that would not admit its length cannot write more
        // than the limit to a disk or, worse, to a bucket that charges by the byte. Bounded by the
        // limit itself, so this is never larger than an upload was always allowed to be.
        using var checkedContent = await ReadWithinLimitAsync(content, maximumBytes, cancellationToken);

        var backend = await ActiveBackendAsync(cancellationToken);
        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var path = await backend.WriteAsync(checkedContent, subFolder, storedFileName, cancellationToken);

        return new StoredFile($"{backend.Name}{Separator}{path}", fileName);
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var (backend, path) = Resolve(relativePath);
        return await backend.ReadAsync(path, cancellationToken);
    }

    public async Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var (backend, path) = Resolve(relativePath);
        await backend.DeleteAsync(path, cancellationToken);
    }

    public async Task CheckAsync(string? providerName = null, CancellationToken cancellationToken = default)
    {
        var backend = providerName is null
            ? await ActiveBackendAsync(cancellationToken)
            : Named(providerName);

        await backend.CheckAsync(cancellationToken);
    }

    public async Task<string> CopyToAsync(
        string relativePath, string targetProvider, string subFolder, CancellationToken cancellationToken = default)
    {
        var (source, path) = Resolve(relativePath);
        var target = Named(targetProvider);

        if (source.Name == target.Name) return relativePath;

        await using var content = await source.ReadAsync(path, cancellationToken);

        // The extension and size are not re-checked. These bytes were accepted once already, and
        // an administrator who has since narrowed the allowed types would otherwise find that
        // moving their files quietly refused to bring some of them, which is a worse outcome than
        // a stored file that no longer matches today's rules.
        var storedFileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(storedFileName)) storedFileName = $"{Guid.NewGuid()}";

        var written = await target.WriteAsync(content, subFolder, storedFileName, cancellationToken);

        return $"{target.Name}{Separator}{written}";
    }

    public string ProviderOf(string relativePath) => Resolve(relativePath).Backend.Name;

    /// <summary>
    /// Which backend a stored key belongs to, and the path within it.
    ///
    /// A key with no separator predates this and is local. So is a key whose prefix names a backend
    /// this build does not have, which is what a Windows path like "C:/uploads" would otherwise
    /// look like; treating that as local is both the safe answer and the true one.
    /// </summary>
    private (IFileStorageBackend Backend, string Path) Resolve(string key)
    {
        var separator = key.IndexOf(Separator);

        if (separator > 0)
        {
            var name = key[..separator];
            var backend = backends.FirstOrDefault(b => b.Name == name);
            if (backend is not null) return (backend, key[(separator + 1)..]);
        }

        return (Named("local"), key);
    }

    private IFileStorageBackend Named(string name) =>
        backends.FirstOrDefault(b => b.Name == name)
        ?? throw new BusinessRuleException($"'{name}' is not a storage option this installation has.");

    private async Task<IFileStorageBackend> ActiveBackendAsync(CancellationToken cancellationToken)
    {
        var configured = await settings.GetStringAsync(SettingKeys.StorageProvider, cancellationToken);

        return Named(string.IsNullOrWhiteSpace(configured)
            ? SettingKeys.DefaultStorageProvider
            : configured.Trim());
    }

    private async Task<IReadOnlyCollection<string>> ConfiguredExtensionsAsync(CancellationToken cancellationToken)
    {
        var raw = await settings.GetStringAsync(SettingKeys.AllowedUploadExtensions, cancellationToken);

        return string.IsNullOrWhiteSpace(raw)
            ? _fallback.AllowedExtensions
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task<long> MaximumBytesAsync(CancellationToken cancellationToken)
    {
        var megabytes = await settings.GetIntAsync(SettingKeys.MaxUploadMegabytes, 0, cancellationToken);
        return megabytes > 0 ? megabytes * 1024L * 1024L : _fallback.MaxFileSizeBytes;
    }

    /// <summary>
    /// Copies while counting, so a stream that could not say its length up front, a chunked upload
    /// for instance, still cannot exceed the limit. Without this the size limit would be advisory
    /// for exactly the uploads most likely to abuse it.
    /// </summary>
    private static async Task<MemoryStream> ReadWithinLimitAsync(
        Stream source, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var destination = new MemoryStream();
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;
            if (written > maximumBytes)
            {
                destination.Dispose();
                throw new BusinessRuleException($"Files must be no larger than {maximumBytes / (1024 * 1024)} MB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        destination.Position = 0;
        return destination;
    }
}
