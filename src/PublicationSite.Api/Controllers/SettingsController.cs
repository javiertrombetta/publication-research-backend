using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// System-wide settings. Grouped endpoints rather than a single key/value setter: a group can be
/// validated as a whole, and a client cannot invent a key that nothing reads and then believe it
/// has configured something. The flat listing remains for support and diagnosis.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize(Roles = RoleNames.Admin)]
public class SettingsController(
    ISystemSettingService systemSettingService,
    IEthicsDocumentRequirementService ethicsRequirementService,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SystemSettingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var result = await systemSettingService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<SystemSettingDto>>.Ok(result));
    }

    // ---------- Committees ----------

    [HttpGet("committees")]
    [ProducesResponseType(typeof(ApiResponse<CommitteeSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommittees()
    {
        var result = await systemSettingService.GetCommitteeSettingsAsync();
        return Ok(ApiResponse<CommitteeSettingsDto>.Ok(result));
    }

    [HttpPut("committees")]
    [ProducesResponseType(typeof(ApiResponse<CommitteeSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCommittees([FromBody] UpdateCommitteeSettingsRequest request)
    {
        var result = await systemSettingService.UpdateCommitteeSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<CommitteeSettingsDto>.Ok(result,
            "Saved. Publications opened from now on will use these figures."));
    }

    // ---------- Passwords ----------

    [HttpGet("passwords")]
    [ProducesResponseType(typeof(ApiResponse<PasswordSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPasswords()
    {
        var result = await systemSettingService.GetPasswordSettingsAsync();
        return Ok(ApiResponse<PasswordSettingsDto>.Ok(result));
    }

    [HttpPut("passwords")]
    [ProducesResponseType(typeof(ApiResponse<PasswordSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePasswords([FromBody] UpdatePasswordSettingsRequest request)
    {
        var result = await systemSettingService.UpdatePasswordSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<PasswordSettingsDto>.Ok(result,
            "Saved. The new rules apply the next time anyone sets a password."));
    }

    // ---------- Notifications ----------

    [HttpGet("notifications")]
    [ProducesResponseType(typeof(ApiResponse<NotificationSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications()
    {
        var result = await systemSettingService.GetNotificationSettingsAsync();
        return Ok(ApiResponse<NotificationSettingsDto>.Ok(result));
    }

    [HttpPut("notifications")]
    [ProducesResponseType(typeof(ApiResponse<NotificationSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateNotifications([FromBody] UpdateNotificationSettingsRequest request)
    {
        var result = await systemSettingService.UpdateNotificationSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<NotificationSettingsDto>.Ok(result, "Saved."));
    }

    // ---------- Ethics documents ----------

    /// <summary>
    /// The documents the ethics stage asks students for. Retired ones are included so an
    /// administrator can see what was once required and bring it back if needed.
    /// </summary>
    [HttpGet("ethics-documents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EthicsDocumentRequirementDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEthicsDocuments()
    {
        var result = await ethicsRequirementService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<EthicsDocumentRequirementDto>>.Ok(result));
    }

    [HttpPost("ethics-documents")]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentRequirementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateEthicsDocument([FromBody] SaveEthicsDocumentRequirementRequest request)
    {
        var result = await ethicsRequirementService.CreateAsync(request, currentUser.UserId);
        return Ok(ApiResponse<EthicsDocumentRequirementDto>.Ok(result,
            "Added. Publications whose ethics stage starts from now on will be asked for it."));
    }

    [HttpPut("ethics-documents/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentRequirementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEthicsDocument(Guid id, [FromBody] SaveEthicsDocumentRequirementRequest request)
    {
        var result = await ethicsRequirementService.UpdateAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse<EthicsDocumentRequirementDto>.Ok(result, "Saved."));
    }

    /// <summary>
    /// Retires a document or brings it back. There is deliberately no delete: a document that
    /// has been asked of anyone is referenced by what they uploaded.
    /// </summary>
    [HttpPut("ethics-documents/{id:guid}/active")]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentRequirementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEthicsDocumentActive(Guid id, [FromQuery] bool isActive)
    {
        var result = await ethicsRequirementService.SetActiveAsync(id, isActive, currentUser.UserId);
        return Ok(ApiResponse<EthicsDocumentRequirementDto>.Ok(result,
            isActive ? "This document will be asked for again." : "This document will no longer be asked for."));
    }

    // ---------- Access ----------

    [HttpGet("access")]
    [ProducesResponseType(typeof(ApiResponse<AccessSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccess()
    {
        var result = await systemSettingService.GetAccessSettingsAsync();
        return Ok(ApiResponse<AccessSettingsDto>.Ok(result));
    }

    [HttpPut("access")]
    [ProducesResponseType(typeof(ApiResponse<AccessSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAccess([FromBody] UpdateAccessSettingsRequest request)
    {
        var result = await systemSettingService.UpdateAccessSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<AccessSettingsDto>.Ok(result, "Saved."));
    }

    // ---------- Uploads ----------

    [HttpGet("uploads")]
    [ProducesResponseType(typeof(ApiResponse<UploadSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUploads()
    {
        var result = await systemSettingService.GetUploadSettingsAsync();
        return Ok(ApiResponse<UploadSettingsDto>.Ok(result));
    }

    [HttpPut("uploads")]
    [ProducesResponseType(typeof(ApiResponse<UploadSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUploads([FromBody] UpdateUploadSettingsRequest request)
    {
        var result = await systemSettingService.UpdateUploadSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<UploadSettingsDto>.Ok(result, "Saved. Applies to the next file anyone uploads."));
    }

    // ---------- The institution ----------

    /// <summary>
    /// Anonymous: the sign-in page, the footer and the public catalogue all need the
    /// institution's name and contact addresses before anyone has signed in.
    /// </summary>
    [HttpGet("institution")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<InstitutionSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInstitution()
    {
        var result = await systemSettingService.GetInstitutionSettingsAsync();
        return Ok(ApiResponse<InstitutionSettingsDto>.Ok(result));
    }

    [HttpPut("institution")]
    [ProducesResponseType(typeof(ApiResponse<InstitutionSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateInstitution([FromBody] UpdateInstitutionSettingsRequest request)
    {
        var result = await systemSettingService.UpdateInstitutionSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<InstitutionSettingsDto>.Ok(result, "Saved."));
    }

    // ---------- Deadlines ----------

    [HttpGet("deadlines")]
    [ProducesResponseType(typeof(ApiResponse<DeadlineSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeadlines()
    {
        var result = await systemSettingService.GetDeadlineSettingsAsync();
        return Ok(ApiResponse<DeadlineSettingsDto>.Ok(result));
    }

    [HttpPut("deadlines")]
    [ProducesResponseType(typeof(ApiResponse<DeadlineSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDeadlines([FromBody] UpdateDeadlineSettingsRequest request)
    {
        var result = await systemSettingService.UpdateDeadlineSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<DeadlineSettingsDto>.Ok(result, "Saved."));
    }
}
