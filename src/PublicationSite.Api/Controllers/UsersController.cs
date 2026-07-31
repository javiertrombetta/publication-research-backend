using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserService userService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var result = await userService.GetOwnProfileAsync(currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateMyProfileRequest request)
    {
        var result = await userService.UpdateOwnProfileAsync(currentUser.UserId, request);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    /// <summary>Any signed-in user manages their own profile photo — this is not role-specific.</summary>
    [HttpPost("me/photo")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadMyPhoto([FromForm] ProfilePhotoUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await userService.SetOwnProfilePhotoAsync(currentUser.UserId, stream, form.File.FileName, form.File.Length);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Profile photo updated."));
    }

    [HttpDelete("me/photo")]
    public async Task<IActionResult> DeleteMyPhoto()
    {
        var result = await userService.RemoveOwnProfilePhotoAsync(currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Profile photo removed."));
    }

    /// <summary>Streams a user's photo. Any signed-in user may read it, so avatars can be shown
    /// wherever people appear in the workflow — 404 when that user has none.</summary>
    [HttpGet("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id)
    {
        var (content, contentType) = await userService.OpenProfilePhotoAsync(id);
        return File(content, contentType);
    }

    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> GetAll([FromQuery] string? role, [FromQuery] string? status, [FromQuery] string? search)
    {
        var result = await userService.GetAllAsync(role, status, search);
        return Ok(ApiResponse<IReadOnlyList<UserListItemDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await userService.GetByIdAsync(id);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var result = await userService.CreateAsync(request, currentUser.UserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<UserDetailDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var result = await userService.UpdateAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse<UserDetailDto>.Ok(result));
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeUserRoleRequest request)
    {
        await userService.ChangeRoleAsync(id, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Role updated."));
    }

    [HttpPut("{id:guid}/enable")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Enable(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.EnableAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User enabled."));
    }

    [HttpPut("{id:guid}/disable")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Disable(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.DisableAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User disabled."));
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.ResetPasswordAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Password reset email sent."));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] CommentsRequest request)
    {
        await userService.DeleteAsync(id, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("User deleted."));
    }
}
