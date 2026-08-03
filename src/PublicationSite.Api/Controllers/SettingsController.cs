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
    IStorageMigrationService storageMigration,
    ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Every configurable setting in one response, for the administrator's settings screen.
    /// </summary>
    /// <response code="200">The matching settings, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SystemSettingDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll()
    {
        var result = await systemSettingService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<SystemSettingDto>>.Ok(result));
    }

    // ---------- Committees ----------

    /// <summary>
    /// How many reviewers and external members a committee needs by default.
    /// </summary>
    /// <response code="200">The committee settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("committees")]
    [ProducesResponseType(typeof(ApiResponse<CommitteeSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCommittees()
    {
        var result = await systemSettingService.GetCommitteeSettingsAsync();
        return Ok(ApiResponse<CommitteeSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes those defaults. Publications already open keep the figures they were opened
    /// under.
    /// </summary>
    /// <response code="200">The committee settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("committees")]
    [ProducesResponseType(typeof(ApiResponse<CommitteeSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCommittees([FromBody] UpdateCommitteeSettingsRequest request)
    {
        var result = await systemSettingService.UpdateCommitteeSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<CommitteeSettingsDto>.Ok(result,
            "Saved. Publications opened from now on will use these figures."));
    }

    // ---------- Passwords ----------

    /// <summary>
    /// The password rules accounts are held to: length, and which kinds of character are required.
    /// </summary>
    /// <response code="200">The password settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("passwords")]
    [ProducesResponseType(typeof(ApiResponse<PasswordSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPasswords()
    {
        var result = await systemSettingService.GetPasswordSettingsAsync();
        return Ok(ApiResponse<PasswordSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes those rules. They apply when a password is next set; existing passwords are not
    /// invalidated, since nobody can be locked out of an account by a rule change they never
    /// saw.
    /// </summary>
    /// <response code="200">The password settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("passwords")]
    [ProducesResponseType(typeof(ApiResponse<PasswordSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdatePasswords([FromBody] UpdatePasswordSettingsRequest request)
    {
        var result = await systemSettingService.UpdatePasswordSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<PasswordSettingsDto>.Ok(result,
            "Saved. The new rules apply the next time anyone sets a password."));
    }

    // ---------- Notifications ----------

    /// <summary>
    /// Which events send an email, and which only raise a notification in the application.
    /// </summary>
    /// <response code="200">The notification settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(ApiResponse<NotificationSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNotifications()
    {
        var result = await systemSettingService.GetNotificationSettingsAsync();
        return Ok(ApiResponse<NotificationSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes that. Turning email off does not lose the notification, which still appears in the
    /// bell.
    /// </summary>
    /// <response code="200">The notification settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("notifications")]
    [ProducesResponseType(typeof(ApiResponse<NotificationSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
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
    /// <response code="200">The matching ethics document requirements, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("ethics-documents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EthicsDocumentRequirementDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEthicsDocuments()
    {
        var result = await ethicsRequirementService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<EthicsDocumentRequirementDto>>.Ok(result));
    }

    /// <summary>
    /// Adds a document to that list, available to ethics decisions made from now on.
    /// </summary>
    /// <response code="200">The ethics document requirement.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    [HttpPost("ethics-documents")]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentRequirementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateEthicsDocument([FromBody] SaveEthicsDocumentRequirementRequest request)
    {
        var result = await ethicsRequirementService.CreateAsync(request, currentUser.UserId);
        return Ok(ApiResponse<EthicsDocumentRequirementDto>.Ok(result,
            "Added. Publications whose ethics stage starts from now on will be asked for it."));
    }

    /// <summary>
    /// Renames or re-describes a document. Publications that already recorded it keep the name they
    /// recorded.
    /// </summary>
    /// <response code="200">The ethics document requirement.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No ethics document requirement with that id.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    [HttpPut("ethics-documents/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentRequirementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateEthicsDocument(Guid id, [FromBody] SaveEthicsDocumentRequirementRequest request)
    {
        var result = await ethicsRequirementService.UpdateAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse<EthicsDocumentRequirementDto>.Ok(result, "Saved."));
    }

    /// <summary>
    /// Retires a document or brings it back. There is deliberately no delete: a document that
    /// has been asked of anyone is referenced by what they uploaded.
    /// </summary>
    /// <response code="200">The ethics document requirement.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No ethics document requirement with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("ethics-documents/{id:guid}/active")]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentRequirementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetEthicsDocumentActive(Guid id, [FromQuery] bool isActive)
    {
        var result = await ethicsRequirementService.SetActiveAsync(id, isActive, currentUser.UserId);
        return Ok(ApiResponse<EthicsDocumentRequirementDto>.Ok(result,
            isActive ? "This document will be asked for again." : "This document will no longer be asked for."));
    }

    // ---------- Access ----------

    /// <summary>
    /// Who may see what without signing in, and whether registration is open.
    /// </summary>
    /// <response code="200">The access settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("access")]
    [ProducesResponseType(typeof(ApiResponse<AccessSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAccess()
    {
        var result = await systemSettingService.GetAccessSettingsAsync();
        return Ok(ApiResponse<AccessSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes that, including whether the public catalogue exists for anonymous visitors at all.
    /// </summary>
    /// <response code="200">The access settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("access")]
    [ProducesResponseType(typeof(ApiResponse<AccessSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAccess([FromBody] UpdateAccessSettingsRequest request)
    {
        var result = await systemSettingService.UpdateAccessSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<AccessSettingsDto>.Ok(result, "Saved."));
    }

    // ---------- Uploads ----------

    /// <summary>
    /// The file types and size limits accepted for papers and for ethics documents.
    /// </summary>
    /// <response code="200">The upload settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("uploads")]
    [ProducesResponseType(typeof(ApiResponse<UploadSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUploads()
    {
        var result = await systemSettingService.GetUploadSettingsAsync();
        return Ok(ApiResponse<UploadSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes those limits. They are applied at upload, so files already accepted stay.
    /// </summary>
    /// <response code="200">The upload settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("uploads")]
    [ProducesResponseType(typeof(ApiResponse<UploadSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
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
    /// <response code="200">The institution settings.</response>
    [HttpGet("institution")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<InstitutionSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInstitution()
    {
        var result = await systemSettingService.GetInstitutionSettingsAsync();
        return Ok(ApiResponse<InstitutionSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes those details.
    /// </summary>
    /// <response code="200">The institution settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("institution")]
    [ProducesResponseType(typeof(ApiResponse<InstitutionSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateInstitution([FromBody] UpdateInstitutionSettingsRequest request)
    {
        var result = await systemSettingService.UpdateInstitutionSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<InstitutionSettingsDto>.Ok(result, "Saved."));
    }

    // ---------- Where uploaded files are kept ----------

    /// <summary>
    /// Where ethics documents, research paper versions and profile photos are written.
    ///
    /// The S3 secret key and the Azure connection string come back as a flag saying whether one is
    /// stored, never as the value: an administrator's browser is not somewhere a storage key
    /// belongs.
    /// </summary>
    /// <response code="200">The storage settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("storage")]
    [ProducesResponseType(typeof(ApiResponse<StorageSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStorage(CancellationToken cancellationToken)
    {
        var result = await systemSettingService.GetStorageSettingsAsync(cancellationToken);
        return Ok(ApiResponse<StorageSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Points new uploads at a destination.
    ///
    /// Files already stored are unaffected and stay readable: each one records the destination
    /// that wrote it, so this is a change to where the next upload goes and nothing else. There is
    /// no migration to run and no moment at which downloads stop working.
    /// </summary>
    /// <response code="200">The storage settings as saved.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the destination is not one this installation has, or it is missing something it cannot work without.</response>
    [HttpPut("storage")]
    [ProducesResponseType(typeof(ApiResponse<StorageSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateStorage(
        [FromBody] UpdateStorageSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await systemSettingService.UpdateStorageSettingsAsync(
            request, currentUser.UserId, cancellationToken);

        return Ok(ApiResponse<StorageSettingsDto>.Ok(result, "Saved."));
    }

    /// <summary>
    /// Tries a destination and says what happened: writes something small and removes it again.
    ///
    /// A failure comes back as a 200 saying no, because the administrator asked a question and
    /// that is the answer to it. An error status here would read as the settings screen being
    /// broken rather than the bucket.
    /// </summary>
    /// <response code="200">Whether it answered, and what it said if it did not.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpPost("storage/check")]
    [ProducesResponseType(typeof(ApiResponse<StorageCheckResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckStorage([FromQuery] string? provider, CancellationToken cancellationToken)
    {
        var result = await systemSettingService.CheckStorageAsync(provider, cancellationToken);
        return Ok(ApiResponse<StorageCheckResultDto>.Ok(result));
    }

    /// <summary>
    /// Copies files stored elsewhere to the destination in force, and repoints their records.
    ///
    /// Optional, and never a side effect of changing the destination: files record where they were
    /// written and keep opening from there, so an installation that never runs this works exactly
    /// as well. This is for wanting everything in one place.
    ///
    /// Copy, repoint, leave the original. In that order an interruption leaves every record
    /// pointing at a file that exists, which is what makes it safe to stop and run again. One run
    /// is bounded so the request answers; if anything is left, run it again.
    /// </summary>
    /// <response code="200">How many moved, how many are left, and anything that could not be copied.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpPost("storage/migrate")]
    [ProducesResponseType(typeof(ApiResponse<StorageMigrationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MigrateStorage(CancellationToken cancellationToken)
    {
        var result = await storageMigration.MigrateToActiveAsync(currentUser.UserId, cancellationToken);

        return Ok(ApiResponse<StorageMigrationResultDto>.Ok(result,
            result.Remaining > 0
                ? $"{result.Moved} copied. {result.Remaining} still to go: run it again to continue."
                : $"{result.Moved} copied. Everything is now at the destination in force."));
    }

    // ---------- Deadlines ----------

    /// <summary>
    /// The dates set for each stage of the cycle.
    /// </summary>
    /// <response code="200">The deadline settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("deadlines")]
    [ProducesResponseType(typeof(ApiResponse<DeadlineSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDeadlines()
    {
        var result = await systemSettingService.GetDeadlineSettingsAsync();
        return Ok(ApiResponse<DeadlineSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Changes those dates.
    /// </summary>
    /// <response code="200">The deadline settings.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("deadlines")]
    [ProducesResponseType(typeof(ApiResponse<DeadlineSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateDeadlines([FromBody] UpdateDeadlineSettingsRequest request)
    {
        var result = await systemSettingService.UpdateDeadlineSettingsAsync(request, currentUser.UserId);
        return Ok(ApiResponse<DeadlineSettingsDto>.Ok(result, "Saved."));
    }
}
