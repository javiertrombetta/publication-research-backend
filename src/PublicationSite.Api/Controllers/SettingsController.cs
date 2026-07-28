using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = RoleNames.Admin)]
public class SettingsController(ISystemSettingService systemSettingService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await systemSettingService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<SystemSettingDto>>.Ok(result));
    }

    [HttpPut]
    public async Task<IActionResult> Set([FromBody] SetSystemSettingRequest request)
    {
        var result = await systemSettingService.SetAsync(request, currentUser.UserId);
        return Ok(ApiResponse<SystemSettingDto>.Ok(result));
    }
}
