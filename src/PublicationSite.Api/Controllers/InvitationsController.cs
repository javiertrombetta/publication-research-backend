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
    /// <summary>
    /// Every invitation and where it stands — pending, accepted, expired or withdrawn.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserInvitationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var result = await invitationService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<UserInvitationDto>>.Ok(result));
    }

    /// <summary>
    /// Invites somebody to an account, with their role fixed at the moment of sending so they
    /// cannot choose their own. This is how anyone gets an account wherever self-registration
    /// is closed, and the only route there has ever been for external committee members, who
    /// are outside the institution and have no address it could recognise.
    /// </summary>
    /// <remarks>
    /// A department is required for the roles that belong to one. If the invitation cannot be
    /// emailed the response says so — the invitation exists, and the trail records it, so it
    /// can be sent again once a mail server is configured.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserInvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request)
    {
        var result = await invitationService.CreateAsync(request, currentUser.UserId);
        return Ok(ApiResponse<UserInvitationDto>.Ok(result, $"Invitation sent to {result.Email}."));
    }

    /// <summary>
    /// Sends it again with a fresh token, which retires the previous link. Re-sending because
    /// an email went astray should not leave two live ways in.
    /// </summary>
    [HttpPost("{id:guid}/resend")]
    [ProducesResponseType(typeof(ApiResponse<UserInvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(Guid id)
    {
        var result = await invitationService.ResendAsync(id, currentUser.UserId);
        return Ok(ApiResponse<UserInvitationDto>.Ok(result,
            $"Sent again to {result.Email}. The previous link no longer works."));
    }

    /// <summary>
    /// Withdraws an unaccepted invitation, killing the link. Refused once it has been accepted:
    /// what exists then is an account, and accounts are disabled or deleted rather than
    /// uninvited.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(typeof(ApiResponse<UserInvitationDto>), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(ApiResponse<InvitationPreviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview([FromQuery] string token)
    {
        var result = await invitationService.PreviewAsync(token);
        return Ok(ApiResponse<InvitationPreviewDto>.Ok(result));
    }

    /// <summary>
    /// Turns an invitation into an account: the invited person sets their password and is given the
    /// role the invitation was sent for. Open without a session, because the person accepting has no
    /// account yet — the token in the link is what proves they were invited.
    /// </summary>
    [HttpPost("accept")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        await invitationService.AcceptAsync(request);
        return Ok(ApiResponse.Ok("Your account is ready. You can sign in now."));
    }
}
