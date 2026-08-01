using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations.Storage;

/// <summary>
/// Files in an S3 bucket.
///
/// Written against the S3 API rather than against Amazon, because that API is what everything else
/// implements. Leaving the service URL empty reaches Amazon; setting it reaches MinIO, Wasabi,
/// Backblaze B2, DigitalOcean Spaces and the rest, which is why there is no separate backend for
/// any of them.
///
/// The client is built per call from the settings in force. Uploads are not frequent enough for
/// that to matter, and holding one would mean an administrator changing a key had no effect until
/// the application was restarted.
/// </summary>
public class S3FileStorageBackend(ISystemSettingsProvider settings) : IFileStorageBackend
{
    public string Name => "s3";

    public async Task<string> WriteAsync(
        Stream content, string subFolder, string storedFileName, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await ConnectAsync(cancellationToken);
        using var _ = client;

        var key = $"{subFolder.Trim('/')}/{storedFileName}";

        try
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                // Left to the caller's own limit rather than streamed blind: the stream handed in
                // has already been checked and copied, so its length is known here.
                AutoCloseStream = false
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new BusinessRuleException($"The file could not be stored in S3. {ex.Message}");
        }

        return key;
    }

    public async Task<Stream> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await ConnectAsync(cancellationToken);
        using var _ = client;

        try
        {
            var response = await client.GetObjectAsync(bucket, path, cancellationToken);

            // Copied out before the client is disposed. The response stream is tied to the
            // connection, and handing it back would give the caller a stream that closes under it.
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NotFoundException("File", path);
        }
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await ConnectAsync(cancellationToken);
        using var _ = client;

        // S3 treats deleting something that is not there as a success, which is the behaviour
        // this interface asks for anyway.
        await client.DeleteObjectAsync(bucket, path, cancellationToken);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var (client, bucket) = await ConnectAsync(cancellationToken);
        using var _ = client;

        try
        {
            // Listing nothing: enough to prove the bucket is there and the key is accepted,
            // without reading anything that happens to be in it.
            await client.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 }, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new BusinessRuleException($"S3 refused the connection. {ex.Message}");
        }
    }

    private async Task<(AmazonS3Client Client, string Bucket)> ConnectAsync(CancellationToken cancellationToken)
    {
        var bucket = await settings.GetStringAsync(SettingKeys.StorageS3Bucket, cancellationToken);
        var accessKey = await settings.GetStringAsync(SettingKeys.StorageS3AccessKeyId, cancellationToken);
        var secretKey = await settings.GetStringAsync(SettingKeys.StorageS3SecretKey, cancellationToken);
        var region = await settings.GetStringAsync(SettingKeys.StorageS3Region, cancellationToken);
        var serviceUrl = await settings.GetStringAsync(SettingKeys.StorageS3ServiceUrl, cancellationToken);
        var forcePathStyle = await settings.GetBoolAsync(
            SettingKeys.StorageS3ForcePathStyle, SettingKeys.DefaultStorageS3ForcePathStyle, cancellationToken);

        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new BusinessRuleException("No S3 bucket has been configured in System settings.");
        }

        var config = new AmazonS3Config { ForcePathStyle = forcePathStyle };

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region.Trim());
        }
        else
        {
            throw new BusinessRuleException(
                "S3 needs either a region or a service URL. Set one in System settings.");
        }

        // No keys means the machine's own credentials: an instance role, or whatever the SDK finds
        // in the environment. That is how a deployment inside AWS is meant to be set up, and it is
        // better than an administrator pasting a long-lived key into a form.
        var client = string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(accessKey.Trim(), secretKey.Trim()), config);

        return (client, bucket.Trim());
    }
}
