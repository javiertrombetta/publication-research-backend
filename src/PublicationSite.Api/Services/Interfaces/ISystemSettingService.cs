using PublicationSite.Api.DTOs.Settings;

namespace PublicationSite.Api.Services.Interfaces;

public interface ISystemSettingService
{
    Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SystemSettingDto> SetAsync(SetSystemSettingRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
}
