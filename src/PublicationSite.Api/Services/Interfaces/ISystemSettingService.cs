using PublicationSite.Api.DTOs.Settings;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// The administrator's write side of the settings table. Grouped rather than key-by-key: a setting
/// only makes sense alongside the ones it interacts with, and validating a group as a whole is the
/// only way to reject a combination that is individually plausible but jointly nonsense, such as a
/// committee needing more approvals than it has members, for instance.
/// </summary>
public interface ISystemSettingService
{
    Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CommitteeSettingsDto> GetCommitteeSettingsAsync(CancellationToken cancellationToken = default);

    Task<CommitteeSettingsDto> UpdateCommitteeSettingsAsync(
        UpdateCommitteeSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<PasswordSettingsDto> GetPasswordSettingsAsync(CancellationToken cancellationToken = default);

    Task<PasswordSettingsDto> UpdatePasswordSettingsAsync(
        UpdatePasswordSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken cancellationToken = default);

    Task<NotificationSettingsDto> UpdateNotificationSettingsAsync(
        UpdateNotificationSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<AccessSettingsDto> GetAccessSettingsAsync(CancellationToken cancellationToken = default);

    Task<AccessSettingsDto> UpdateAccessSettingsAsync(
        UpdateAccessSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<UploadSettingsDto> GetUploadSettingsAsync(CancellationToken cancellationToken = default);

    Task<UploadSettingsDto> UpdateUploadSettingsAsync(
        UpdateUploadSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<InstitutionSettingsDto> GetInstitutionSettingsAsync(CancellationToken cancellationToken = default);

    Task<InstitutionSettingsDto> UpdateInstitutionSettingsAsync(
        UpdateInstitutionSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>Where uploaded files are kept, and the details of each destination.</summary>
    Task<StorageSettingsDto> GetStorageSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Points new uploads at a destination. Files already stored are unaffected.</summary>
    Task<StorageSettingsDto> UpdateStorageSettingsAsync(UpdateStorageSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);

    /// <summary>Tries the destination and reports what happened, rather than throwing.</summary>
    Task<StorageCheckResultDto> CheckStorageAsync(string? provider = null, CancellationToken cancellationToken = default);

    Task<DeadlineSettingsDto> GetDeadlineSettingsAsync(CancellationToken cancellationToken = default);

    Task<DeadlineSettingsDto> UpdateDeadlineSettingsAsync(
        UpdateDeadlineSettingsRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
}
