using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Evaluation committees: appointing one to a submitted paper, the votes its members cast, and how
/// many members of each kind a committee needs.
///
/// A publication is judged by the composition recorded on it when it was opened, not by whatever is
/// configured today. Research that runs for months cannot have its rules changed underneath it.
/// Anyone at the institution except a student may sit on one.
/// </summary>
[ApiController]
[Authorize]
public class CommitteesController(ICommitteeService committeeService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Appoints the evaluation committee for a paper the supervisor has approved. Nothing moves
    /// until this happens, because the coordinator's final decision is blocked on it.
    /// </summary>
    /// <remarks>
    /// Anyone at the institution except a student may be appointed; a committee judges a student's
    /// work, so it cannot be drawn from the people whose work is judged. The composition must match
    /// what the publication was opened under, not what is configured today.
    /// </remarks>
    /// <response code="200">The committee.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the committee nor the publication was found by that id.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/assign-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<CommitteeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Assign(Guid publicationId, [FromBody] AssignCommitteeRequest request)
    {
        var result = await committeeService.AssignAsync(publicationId, request, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result));
    }

    /// <summary>
    /// The committee on a paper, with each member's decision and comments.
    /// </summary>
    /// <response code="200">The committee.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">Neither the committee nor the publication was found by that id.</response>
    [HttpGet("api/publications/{publicationId:guid}/committee")]
    [ProducesResponseType(typeof(ApiResponse<CommitteeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByPublication(Guid publicationId)
    {
        var result = await committeeService.GetByPublicationAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result));
    }

    /// <summary>
    /// Every evaluation committee still sitting, newest first.
    ///
    /// The assignment screen only lists papers with no committee yet, so an appointed one used to
    /// leave every screen there was. This is what an administrator needs to find one whose
    /// membership has to change.
    /// </summary>
    /// <response code="200">One page of committees, with the total count alongside it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/committees/in-progress")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CommitteeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInProgress([FromQuery] PageRequest paging)
    {
        var result = await committeeService.GetInProgressAsync(paging);
        return Ok(ApiResponse<PagedResult<CommitteeDto>>.Ok(result));
    }

    /// <summary>
    /// Changes who sits on a committee and how many approvals it needs, with a reason.
    ///
    /// The list of members is the committee as it should now stand: anyone left out is removed.
    /// Members who stay keep the decision they have already recorded. Refused once the committee
    /// has finished, because its decisions are what the coordinator ruled on.
    /// </summary>
    /// <response code="200">The committee as it now stands.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No committee with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("api/committees/{committeeId:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<CommitteeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid committeeId, [FromBody] UpdateCommitteeRequest request)
    {
        var result = await committeeService.UpdateAsync(committeeId, request, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result, "Committee updated."));
    }

    /// <summary>
    /// Everybody who could be put on a committee right now: holding a role the institution draws
    /// on, enabled, available, and not one of the people an administrator has left out.
    ///
    /// Served by the API rather than worked out by whichever screen is drawing the list, so what is
    /// offered and what will be accepted are the same answer.
    /// </summary>
    /// <response code="200">The candidates, by surname.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/committees/candidates")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CommitteeCandidateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCandidates()
    {
        var result = await committeeService.GetCandidatesAsync();
        return Ok(ApiResponse<IReadOnlyList<CommitteeCandidateDto>>.Ok(result));
    }

    /// <summary>
    /// Whether the person asking could be put on a committee, under the rules as they stand.
    ///
    /// Exists so a client can decide whether to offer committee work at all. Holding one of the
    /// eligible roles is not the whole answer: an administrator chooses which of those roles this
    /// institution draws on and can leave individuals out, and a menu entry that ignores both would
    /// lead somebody to a screen that will never have anything on it.
    /// </summary>
    /// <response code="200">True where they are a candidate.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("api/committees/my-eligibility")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyEligibility()
    {
        var result = await committeeService.IsCandidateAsync(currentUser.UserId);
        return Ok(ApiResponse<bool>.Ok(result));
    }

    /// <summary>
    /// The papers this member has been asked to evaluate, the ones still needing their vote
    /// first, with the paper itself carried alongside so the list needs nothing further to be
    /// readable.
    /// </summary>
    /// <response code="200">One page of committees, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <param name="awaitingMe">Only the papers this member has still to vote on.</param>
    [HttpGet("api/committees/my-assignments")]
    [Authorize(Roles = RoleNames.CommitteeEligibleRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CommitteeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyAssignments(
        [FromQuery] PageRequest paging, [FromQuery] string? search = null, [FromQuery] bool awaitingMe = false)
    {
        var result = await committeeService.GetAssignmentsForMemberAsync(
            currentUser.UserId, paging, search, awaitingMe);
        return Ok(ApiResponse<PagedResult<CommitteeDto>>.Ok(result));
    }

    /// <summary>
    /// Records this member's own decision and the comments behind it, once. When the last member
    /// has voted the committee is complete and the coordinator is told there is a decision to make.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No committee with that id.</response>
    /// <response code="409">It is already recorded. This has been done, or created, before.</response>
    [HttpPost("api/committees/{committeeId:guid}/review")]
    [Authorize(Roles = RoleNames.CommitteeEligibleRoles)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MemberReview(Guid committeeId, [FromBody] CommitteeMemberReviewRequest request)
    {
        await committeeService.MemberReviewAsync(committeeId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    /// <summary>
    /// How many members of each kind a committee needs by default.
    /// </summary>
    /// <response code="200">The matching committee compositions, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/settings/default-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDefaultConfig()
    {
        var result = await committeeService.GetDefaultConfigAsync();
        return Ok(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>.Ok(result));
    }

    /// <summary>
    /// Changes that default. It applies to publications opened afterwards: each one keeps the
    /// figures it was opened under.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("api/settings/default-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetDefaultConfig([FromBody] SetCommitteeRoleConfigRequest request)
    {
        await committeeService.SetDefaultConfigAsync(request);
        return Ok(ApiResponse.Ok("Default committee configuration updated."));
    }

    /// <summary>
    /// The composition one particular committee was built to.
    /// </summary>
    /// <response code="200">The matching committee compositions, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/committees/{committeeId:guid}/config")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCommitteeConfig(Guid committeeId)
    {
        var result = await committeeService.GetCommitteeConfigAsync(committeeId);
        return Ok(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>.Ok(result));
    }

    /// <summary>
    /// Overrides the composition for one committee, for a paper that genuinely needs a
    /// different panel from the standard one.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No committee with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("api/committees/{committeeId:guid}/config")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetCommitteeConfig(Guid committeeId, [FromBody] SetCommitteeRoleConfigRequest request)
    {
        await committeeService.SetCommitteeConfigAsync(
            committeeId, request, currentUser.UserId, User.IsInRole(RoleNames.Admin));
        return Ok(ApiResponse.Ok("Committee configuration updated."));
    }
}
