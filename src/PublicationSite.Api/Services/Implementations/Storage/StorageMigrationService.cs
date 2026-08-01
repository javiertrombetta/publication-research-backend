using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations.Storage;

/// <summary>
/// Brings files already stored elsewhere to the destination in force.
///
/// Not needed for anything to work: a file records where it was written and keeps opening from
/// there, so an installation can leave its history spread across a disk and a bucket for ever.
/// This is for the administrator who would rather it were all in one place, and it is theirs to
/// ask for rather than something a change of destination does on its own.
///
/// Copy, then repoint, then leave the original. In that order, an interruption at any point leaves
/// every reference pointing at a file that exists: the ones already moved at the new destination,
/// the rest at the old. There is no half-migrated state that loses anything, which is what makes
/// it safe to stop and run again.
///
/// The originals are not deleted. Reclaiming that space is a decision with no undo, and it should
/// be taken deliberately by somebody who has checked the new destination holds what they expect.
/// </summary>
public class StorageMigrationService(
    ApplicationDbContext db,
    IFileStorageService storage,
    ISystemSettingsProvider settings,
    IAuditService auditService,
    ILogger<StorageMigrationService> logger) : IStorageMigrationService
{
    /// <summary>
    /// How many files one run will move. A request has to answer eventually, and every file is a
    /// read and a write of up to the upload limit. Running again continues where this left off,
    /// because a file already at the destination is skipped rather than copied twice.
    /// </summary>
    private const int BatchSize = 200;

    public async Task<StorageMigrationResultDto> MigrateToActiveAsync(
        Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var target = await settings.GetStringAsync(SettingKeys.StorageProvider, cancellationToken);
        if (string.IsNullOrWhiteSpace(target)) target = SettingKeys.DefaultStorageProvider;

        var moved = 0;
        var failed = new List<string>();
        var budget = BatchSize;

        // Ethics documents, keyed by the publication they belong to, which is the folder they
        // were written under in the first place.
        var ethics = await db.EthicsDocuments
            .Select(d => new { d.Id, d.FilePath, Container = d.EthicsApproval.PublicationContainerId })
            .ToListAsync(cancellationToken);

        foreach (var document in ethics)
        {
            if (budget <= 0) break;
            if (storage.ProviderOf(document.FilePath) == target) continue;

            budget--;
            var key = await TryCopyAsync(document.FilePath, target, $"ethics/{document.Container}",
                $"ethics document {document.Id}", failed, cancellationToken);
            if (key is null) continue;

            await db.EthicsDocuments.Where(d => d.Id == document.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.FilePath, key), cancellationToken);
            moved++;
        }

        var versions = await db.PublicationVersions
            .Select(v => new { v.Id, v.FilePath, v.PublicationId })
            .ToListAsync(cancellationToken);

        foreach (var version in versions)
        {
            if (budget <= 0) break;
            if (storage.ProviderOf(version.FilePath) == target) continue;

            budget--;
            var key = await TryCopyAsync(version.FilePath, target, $"papers/{version.PublicationId}",
                $"paper version {version.Id}", failed, cancellationToken);
            if (key is null) continue;

            await db.PublicationVersions.Where(v => v.Id == version.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.FilePath, key), cancellationToken);
            moved++;
        }

        var photos = await db.Users
            .Where(u => u.ProfilePhotoPath != null)
            .Select(u => new { u.Id, Path = u.ProfilePhotoPath! })
            .ToListAsync(cancellationToken);

        foreach (var photo in photos)
        {
            if (budget <= 0) break;
            if (storage.ProviderOf(photo.Path) == target) continue;

            budget--;
            var key = await TryCopyAsync(photo.Path, target, $"profile-photos/{photo.Id}",
                $"profile photo for {photo.Id}", failed, cancellationToken);
            if (key is null) continue;

            await db.Users.Where(u => u.Id == photo.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ProfilePhotoPath, key), cancellationToken);
            moved++;
        }

        var remaining = CountElsewhere(ethics.Select(e => e.FilePath), target)
                        + CountElsewhere(versions.Select(v => v.FilePath), target)
                        + CountElsewhere(photos.Select(p => p.Path), target)
                        - moved - failed.Count;

        if (moved > 0 || failed.Count > 0)
        {
            await auditService.LogAuditAsync(actingAdminId, "StorageFilesMigrated", nameof(SystemSetting), null,
                comments: $"{moved} file(s) copied to {target}. {failed.Count} could not be. "
                          + "The originals were left where they were.");
        }

        return new StorageMigrationResultDto(moved, Math.Max(0, remaining), failed);
    }

    public async Task<int> CountElsewhereAsync(CancellationToken cancellationToken = default)
    {
        var target = await settings.GetStringAsync(SettingKeys.StorageProvider, cancellationToken);
        if (string.IsNullOrWhiteSpace(target)) target = SettingKeys.DefaultStorageProvider;

        var keys = await db.EthicsDocuments.Select(d => d.FilePath)
            .Concat(db.PublicationVersions.Select(v => v.FilePath))
            .Concat(db.Users.Where(u => u.ProfilePhotoPath != null).Select(u => u.ProfilePhotoPath!))
            .ToListAsync(cancellationToken);

        return CountElsewhere(keys, target);
    }

    private int CountElsewhere(IEnumerable<string> keys, string target) =>
        keys.Count(k => storage.ProviderOf(k) != target);

    /// <summary>
    /// One file. A failure is collected rather than thrown: one unreadable file should not stop
    /// the other two hundred, and the administrator needs to know which one it was.
    /// </summary>
    private async Task<string?> TryCopyAsync(
        string key, string target, string subFolder, string describe,
        List<string> failed, CancellationToken cancellationToken)
    {
        try
        {
            return await storage.CopyToAsync(key, target, subFolder, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not copy {What} to {Target}.", describe, target);
            failed.Add($"{describe}: {ex.Message}");
            return null;
        }
    }
}
