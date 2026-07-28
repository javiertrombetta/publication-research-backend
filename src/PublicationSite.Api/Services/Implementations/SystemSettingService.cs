using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class SystemSettingService(ApplicationDbContext db) : ISystemSettingService
{
    public async Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await db.SystemSettings
            .OrderBy(s => s.Key)
            .Select(s => new SystemSettingDto(s.Id, s.Key, s.Value, s.Description, s.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SystemSettingDto> SetAsync(SetSystemSettingRequest request, Guid actingAdminId, CancellationToken cancellationToken = default)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == request.Key, cancellationToken);

        if (setting is null)
        {
            setting = new SystemSetting { Key = request.Key };
            db.SystemSettings.Add(setting);
        }

        setting.Value = request.Value;
        setting.Description = request.Description;
        setting.UpdatedByUserId = actingAdminId;
        setting.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new SystemSettingDto(setting.Id, setting.Key, setting.Value, setting.Description, setting.UpdatedAt);
    }
}
