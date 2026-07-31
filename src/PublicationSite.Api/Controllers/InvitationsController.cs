using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Invitations. Administrators send them; the two anonymous actions are how the invited person
/// replies, since by definition they have no account yet.
/// </summary>
[ApiController]
[Route("api/invitations")]
[Authorize(Roles = RoleNames.Admin)]
public class InvitationsController(IInvitationService invitationService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await invitationService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<UserInvitationDto>>.Ok(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request)
    {
        var result = await invitationService.CreateAsync(request, currentUser.UserId);
        return Ok(ApiResponse<UserInvitationDto>.Ok(result, $"Invitation sent to {result.Email}."));
    }

    [HttpPost("{id:guid}/resend")]
    public async Task<IActionResult> Resend(Guid id)
    {
        var result = await invitationService.ResendAsync(id, currentUser.UserId);
        return Ok(ApiResponse<UserInvitationDto>.Ok(result,
            $"Sent again to {result.Email}. The previous link no longer works."));
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var result = await invitationService.RevokeAsync(id, currentUser.UserId);
        return Ok(ApiResponse<UserInvitationDto>.Ok(result, "Invitation withdrawn."));
    }

    // ---------- What the invited person calls ----------

    /// <summary>
    /// Anonymous by necessity: the whole point is that this person has no account. The token is
    /// the only credential, and it is unguessable and single-use.
    /// </summary>
    [HttpGet("preview")]
    [AllowAnonymous]
    public async Task<IActionResult> Preview([FromQuery] string token)
    {
        var result = await invitationService.PreviewAsync(token);
        return Ok(ApiResponse<InvitationPreviewDto>.Ok(result));
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        await invitationService.AcceptAsync(request);
        return Ok(ApiResponse.Ok("Your account is ready. You can sign in now."));
    }
}
