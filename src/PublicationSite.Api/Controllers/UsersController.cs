using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Accounts and the profiles attached to them. The role is the authority; the profile carries the
/// attributes that role needs, and is kept rather than deleted when somebody's role changes, so what
/// they did in it still reads correctly.
///
/// Most of this is an administrator's. The exceptions are the ones anybody uses on themselves: their
/// own profile, their own photo, their own password.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserService userService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// The signed-in user's own account and whichever profile their role carries.
    /// </summary>
    /// <response code="200">The account.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe()
    {
        var result = await userService.GetOwnProfileAsync(currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>
    /// Updates what somebody may change about themselves. Their department, student number and role
    /// are not on that list, because those describe their standing at the institution and an
    /// administrator maintains them.
    /// </summary>
    /// <response code="200">The account.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateMyProfileRequest request)
    {
        var result = await userService.UpdateOwnProfileAsync(currentUser.UserId, request);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>Any signed-in user manages their own profile photo, so this is not role-specific.</summary>
    /// <response code="200">The account.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="404">No application user with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("me/photo")]
    [RequestSizeLimit(10_000_000)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadMyPhoto([FromForm] ProfilePhotoUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await userService.SetOwnProfilePhotoAsync(currentUser.UserId, stream, form.File.FileName, form.File.Length);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Profile photo updated."));
    }

    /// <summary>
    /// Removes it, and deletes the stored file rather than only forgetting where it was.
    /// </summary>
    /// <response code="200">The account.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpDelete("me/photo")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteMyPhoto()
    {
        var result = await userService.RemoveOwnProfilePhotoAsync(currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Profile photo removed."));
    }

    /// <summary>Streams a user's photo. Any signed-in user may read it, so avatars can be shown
    /// wherever people appear in the workflow. 404 when that user has none.</summary>
    /// <response code="200">The file itself, as an attachment.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpGet("{id:guid}/photo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid id)
    {
        var (content, contentType) = await userService.OpenProfilePhotoAsync(id);
        return File(content, contentType);
    }

    /// <summary>
    /// The user directory, filtered by role, status and a search over name and address.
    /// </summary>
    /// <response code="200">The matching accounts, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] string? role, [FromQuery] string? status, [FromQuery] string? search)
    {
        var result = await userService.GetAllAsync(role, status, search);
        return Ok(ApiResponse<IReadOnlyList<UserListItemDto>>.Ok(result));
    }

    /// <summary>
    /// The supervisors a Coordinator can send proposals to. Narrower than the full user list,
    /// which stays Admin-only: assigning supervision needs exactly this and nothing more, and
    /// there is no reason for a Coordinator to be able to enumerate students or administrators.
    /// Only enabled accounts, since a disabled one cannot take the work.
    /// </summary>
    /// <response code="200">The matching accounts, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("supervisors")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSupervisors([FromQuery] string? search)
    {
        var result = await userService.GetAllAsync(RoleNames.Supervisor, nameof(UserStatus.Enabled), search);
        return Ok(ApiResponse<IReadOnlyList<UserListItemDto>>.Ok(result));
    }

    /// <summary>
    /// One account in full, with its profile and its history.
    /// </summary>
    /// <response code="200">The account.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await userService.GetByIdAsync(id);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>
    /// Creates an account directly, for when inviting somebody is not what is wanted. The role is
    /// given rather than derived, and the roles that belong to a department require one.
    /// </summary>
    /// <response code="201">The account was created. Its id is in the body, and the Location header points at it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var (user, passwordEmailSent) = await userService.CreateAsync(request, currentUser.UserId);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ApiResponse<UserDetailDto>.Ok(user,
            passwordEmailSent
                ? $"Account created. {user.Email} has been emailed a link to set their password."
                : $"Account created, but {user.Email} could not be emailed a link to set their password. " +
                  "Check the mail server under System settings, then reset their password to send it again."));
    }

    /// <summary>
    /// Corrects somebody's name or institutional identifier. Requires a reason, which is
    /// recorded.
    /// </summary>
    /// <response code="200">The account.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var result = await userService.UpdateAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>
    /// Moves an account to a different role, creating whatever profile the new role needs before
    /// the role itself changes. A role granted without the profile it depends on leaves an account
    /// that cannot do its job.
    /// </summary>
    /// <remarks>
    /// Profiles are never deleted on the way out. Work already allocated points at them, so the old
    /// one is kept and simply stops being used.
    /// </remarks>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request)
    {
        await userService.ChangeRoleAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Role updated."));
    }

    /// <summary>
    /// Restores a disabled account.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpPut("{id:guid}/enable")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Enable(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.EnableAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User enabled."));
    }

    /// <summary>
    /// Stops an account signing in, without removing anything it is attached to. The reversible
    /// half of taking somebody out of the system.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpPut("{id:guid}/disable")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disable(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.DisableAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User disabled."));
    }

    /// <summary>
    /// Sets a password on somebody's behalf, for when they cannot use the emailed reset: a new
    /// external member who never received it, or an address that no longer works.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.ResetPasswordAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Password reset email sent."));
    }

    /// <summary>
    /// Retires an account for good: it is anonymised and locked rather than removed, because
    /// everything it touched refers to it and must keep making sense: proposals, reviews,
    /// decisions, the audit trail. Requires a reason, which is recorded against the trail that
    /// outlives the account.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No application user with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.DeleteAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User deleted."));
    }
}
