using Azure;
using Azure.Storage.Blobs;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations.Storage;

/// <summary>
/// Files in an Azure Blob Storage container.
///
/// The container is created if it is not there, so an administrator who has a storage account but
/// has not thought about containers still gets a working setup. It is created private: these are
/// ethics documents and unpublished research papers, and a public container would put them on the
/// open web behind a guessable URL.
/// </summary>
public class AzureBlobFileStorageBackend(ISystemSettingsProvider settings) : IFileStorageBackend
{
    public string Name => "azure-blob";

    public async Task<string> WriteAsync(
        Stream content, string subFolder, string storedFileName, CancellationToken cancellationToken = default)
    {
        var container = await ConnectAsync(cancellationToken);
        var path = $"{subFolder.Trim('/')}/{storedFileName}";

        try
        {
            await container.GetBlobClient(path).UploadAsync(content, overwrite: true, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new BusinessRuleException($"The file could not be stored in Azure Blob Storage. {ex.Message}");
        }

        return path;
    }

    public async Task<Stream> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var container = await ConnectAsync(cancellationToken);

        try
        {
            // Downloaded into memory rather than handed back as a live blob stream, so the caller
            // holds something that does not depend on a connection staying open.
            var buffer = new MemoryStream();
            await container.GetBlobClient(path).DownloadToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new NotFoundException("File", path);
        }
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var container = await ConnectAsync(cancellationToken);
        await container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ConnectAsync(cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new BusinessRuleException($"Azure Blob Storage refused the connection. {ex.Message}");
        }
    }

    private async Task<BlobContainerClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var connectionString = await settings.GetStringAsync(
            SettingKeys.StorageAzureConnectionString, cancellationToken);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new BusinessRuleException(
                "No Azure Blob Storage connection string has been configured in System settings.");
        }

        var name = await settings.GetStringAsync(SettingKeys.StorageAzureContainer, cancellationToken);
        var container = new BlobContainerClient(
            connectionString.Trim(),
            string.IsNullOrWhiteSpace(name) ? SettingKeys.DefaultStorageAzureContainer : name.Trim());

        // Private, and said so rather than left to the account's default.
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        return container;
    }
}
