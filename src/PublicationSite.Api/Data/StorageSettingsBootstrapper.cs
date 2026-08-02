using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Data;

/// <summary>
/// Writes the file storage destination from configuration, once, when the database has no opinion
/// of its own.
///
/// Where uploads go is an administrator's setting and lives in the database, which is right: it can
/// be changed from a screen without a redeploy. The awkward consequence is that a deployment on a
/// host with no durable disk starts out writing to one anyway, and every database reset puts it
/// back there. On a testing deployment that gets reset often, that means reconfiguring storage by
/// hand after every reset and losing every uploaded file in between.
///
/// So the environment may state a starting point. It is only ever a starting point: anything
/// already recorded wins, and an administrator changing it on the screen is never overruled on the
/// next restart. That is the difference between this and configuration that simply governs the
/// setting, which would take the screen away.
/// </summary>
public static class StorageSettingsBootstrapper
{
    /// <summary>
    /// The configuration section, so a deployment writes Storage__Provider and the rest exactly as
    /// it writes every other environment variable.
    /// </summary>
    public const string SectionName = "Storage";

    private static readonly (string Setting, string Configuration)[] Map =
    [
        (SettingKeys.StorageProvider, "Provider"),
        (SettingKeys.StorageLocalPath, "LocalPath"),
        (SettingKeys.StorageS3Bucket, "S3Bucket"),
        (SettingKeys.StorageS3Region, "S3Region"),
        (SettingKeys.StorageS3ServiceUrl, "S3ServiceUrl"),
        (SettingKeys.StorageS3AccessKeyId, "S3AccessKeyId"),
        (SettingKeys.StorageS3SecretKey, "S3SecretKey"),
        (SettingKeys.StorageS3ForcePathStyle, "S3ForcePathStyle"),
        (SettingKeys.StorageAzureContainer, "AzureContainer"),
        (SettingKeys.StorageAzureConnectionString, "AzureConnectionString")
    ];

    public static async Task ApplyAsync(
        IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection(SectionName);
        var provider = section["Provider"];

        // Nothing said, nothing done. A deployment that does not mention storage keeps whatever the
        // database says, which for a fresh one is a directory on the server.
        if (string.IsNullOrWhiteSpace(provider)) return;

        var db = services.GetRequiredService<ApplicationDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(StorageSettingsBootstrapper));

        // Already answered, by a previous startup or by an administrator on the screen. Either way
        // it is not this method's business: configuration proposes a starting point and does not
        // get to keep re-imposing it.
        if (await db.SystemSettings.AnyAsync(s => s.Key == SettingKeys.StorageProvider, cancellationToken))
        {
            return;
        }

        var written = 0;
        foreach (var (key, name) in Map)
        {
            var value = section[name];
            if (string.IsNullOrWhiteSpace(value)) continue;

            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value.Trim(),
                Description = "Set from this deployment's configuration when the database had no value for it."
            });

            written++;
        }

        if (written == 0) return;

        await db.SaveChangesAsync(cancellationToken);
        services.GetRequiredService<ISystemSettingsProvider>().Invalidate();

        logger.LogInformation(
            "File storage set from configuration: new uploads go to {Provider}. An administrator can change "
            + "this in System settings, and this will not overwrite them.", provider);
    }
}
