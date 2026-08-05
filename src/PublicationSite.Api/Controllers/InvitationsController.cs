using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.DTOs.Common;
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
    /// Every invitation and where it stands: pending, accepted, expired or withdrawn.
    /// </summary>
    /// <remarks>
    /// <paramref name="state"/> narrows it to <c>Pending</c> or <c>Settled</c>, which is how the
    /// screen draws its two blocks as two listings rather than one split after the fact. Ordered
    /// newest first, or by <c>person</c>, <c>email</c>, <c>role</c>, <c>department</c>,
    /// <c>invitedby</c>, <c>sent</c> or <c>expires</c>. The search term covers the invited
    /// person's name and their address.
    /// </remarks>
    /// <response code="200">One page of invitations, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserInvitationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] PageRequest paging, [FromQuery] string? state = null, [FromQuery] string? search = null)
    {
        var result = await invitationService.GetAllAsync(paging, state, search);
        return Ok(ApiResponse<PagedResult<UserInvitationDto>>.Ok(result));
    }

    /// <summary>
    /// Invites somebody to an account, with their role fixed at the moment of sending so they
    /// cannot choose their own. This is how anyone gets an account wherever self-registration is
    /// closed, and the only route there has ever been for external committee members, who are
    /// outside the institution and have no address it could recognise.
    /// </summary>
    /// <remarks>
    /// A department is required for the roles that belong to one. If the invitation cannot be
    /// emailed the response says so. The invitation exists, and the trail records it, so it can be
    /// sent again once a mail server is configured.
    /// </remarks>
    /// <response code="200">The user invitation.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request)
    {
        var result = await invitationService.CreateAsync(request, currentUser.UserId);
        return Ok(ApiResponse<UserInvitationDto>.Ok(result, $"Invitation sent to {result.Email}."));
    }

    /// <summary>
    /// Sends it again with a fresh token, which retires the previous link. Re-sending because
    /// an email went astray should not leave two live ways in.
    /// </summary>
    /// <response code="200">The user invitation.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No user invitation with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("{id:guid}/resend")]
    [ProducesResponseType(typeof(ApiResponse<UserInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
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
    /// <response code="200">The user invitation.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No user invitation with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(typeof(ApiResponse<UserInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
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
    /// <response code="200">The invitation preview.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpGet("preview")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<InvitationPreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Preview([FromQuery] string token)
    {
        var result = await invitationService.PreviewAsync(token);
        return Ok(ApiResponse<InvitationPreviewDto>.Ok(result));
    }

    /// <summary>
    /// Turns an invitation into an account: the invited person sets their password and is given the
    /// role the invitation was sent for. Open without a session, because the person accepting has
    /// no account yet. The token in the link is what proves they were invited.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("accept")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        await invitationService.AcceptAsync(request);
        return Ok(ApiResponse.Ok("Your account is ready. You can sign in now."));
    }
}
