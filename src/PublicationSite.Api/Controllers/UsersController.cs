using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserService userService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// The signed-in user's own account and whichever profile their role carries.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMe()
    {
        var result = await userService.GetOwnProfileAsync(currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>
    /// Updates what somebody may change about themselves. Their department, student number and
    /// role are not on that list — those describe their standing at the institution, and an
    /// administrator maintains them.
    /// </summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateMyProfileRequest request)
    {
        var result = await userService.UpdateOwnProfileAsync(currentUser.UserId, request);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>Any signed-in user manages their own profile photo — this is not role-specific.</summary>
    [HttpPost("me/photo")]
    [RequestSizeLimit(10_000_000)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadMyPhoto([FromForm] ProfilePhotoUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await userService.SetOwnProfilePhotoAsync(currentUser.UserId, stream, form.File.FileName, form.File.Length);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Profile photo updated."));
    }

    /// <summary>
    /// Removes it, and deletes the stored file rather than only forgetting where it was.
    /// </summary>
    [HttpDelete("me/photo")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMyPhoto()
    {
        var result = await userService.RemoveOwnProfilePhotoAsync(currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Profile photo removed."));
    }

    /// <summary>Streams a user's photo. Any signed-in user may read it, so avatars can be shown
    /// wherever people appear in the workflow — 404 when that user has none.</summary>
    [HttpGet("{id:guid}/photo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPhoto(Guid id)
    {
        var (content, contentType) = await userService.OpenProfilePhotoAsync(id);
        return File(content, contentType);
    }

    /// <summary>
    /// The user directory, filtered by role, status and a search over name and address.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserListItemDto>>), StatusCodes.Status200OK)]
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
    [HttpGet("supervisors")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupervisors([FromQuery] string? search)
    {
        var result = await userService.GetAllAsync(RoleNames.Supervisor, nameof(UserStatus.Enabled), search);
        return Ok(ApiResponse<IReadOnlyList<UserListItemDto>>.Ok(result));
    }

    /// <summary>
    /// One account in full, with its profile and its history.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await userService.GetByIdAsync(id);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>
    /// Creates an account directly, for when inviting somebody is not what is wanted. The role
    /// is given rather than derived, and the roles that belong to a department require one.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
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
    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var result = await userService.UpdateAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>
    /// Moves an account to a different role, creating whatever profile the new role needs
    /// before the role itself changes — a role granted without the profile it depends on leaves
    /// an account that cannot do its job.
    /// </summary>
    /// <remarks>
    /// Profiles are never deleted on the way out. Work already allocated points at them, so the
    /// old one is kept and simply stops being used.
    /// </remarks>
    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request)
    {
        await userService.ChangeRoleAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Role updated."));
    }

    /// <summary>
    /// Restores a disabled account.
    /// </summary>
    [HttpPut("{id:guid}/enable")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enable(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.EnableAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User enabled."));
    }

    /// <summary>
    /// Stops an account signing in, without removing anything it is attached to. The reversible
    /// half of taking somebody out of the system.
    /// </summary>
    [HttpPut("{id:guid}/disable")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Disable(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.DisableAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User disabled."));
    }

    /// <summary>
    /// Sets a password on somebody's behalf, for when they cannot use the emailed reset — a new
    /// external member who never received it, or an address that no longer works.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.ResetPasswordAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Password reset email sent."));
    }

    /// <summary>
    /// Retires an account for good: it is anonymised and locked rather than removed, because
    /// everything it touched — proposals, reviews, decisions, the audit trail — refers to it
    /// and must keep making sense. Requires a reason, which is recorded against the trail that
    /// outlives the account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.DeleteAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User deleted."));
    }
}
