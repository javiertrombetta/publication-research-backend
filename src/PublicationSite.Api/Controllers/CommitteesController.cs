using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Authorize]
public class CommitteesController(ICommitteeService committeeService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Appoints the evaluation committee for a paper the supervisor has approved. Nothing moves
    /// until this happens — the coordinator's final decision is blocked on it.
    /// </summary>
    /// <remarks>
    /// Anyone at the institution except a student may be appointed; a committee judges a
    /// student's work, so it cannot be drawn from the people whose work is judged. The
    /// composition must match what the publication was opened under, not what is configured
    /// today.
    /// </remarks>
    [HttpPost("api/publications/{publicationId:guid}/assign-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<CommitteeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid publicationId, [FromBody] AssignCommitteeRequest request)
    {
        var result = await committeeService.AssignAsync(publicationId, request, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result));
    }

    /// <summary>
    /// The committee on a paper, with each member's decision and comments.
    /// </summary>
    [HttpGet("api/publications/{publicationId:guid}/committee")]
    [ProducesResponseType(typeof(ApiResponse<CommitteeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPublication(Guid publicationId)
    {
        var result = await committeeService.GetByPublicationAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result));
    }

    /// <summary>
    /// The papers this member has been asked to evaluate, the ones still needing their vote
    /// first, with the paper itself carried alongside so the list needs nothing further to be
    /// readable.
    /// </summary>
    [HttpGet("api/committees/my-assignments")]
    [Authorize(Roles = $"{RoleNames.InternalCommitteeMember},{RoleNames.ExternalCommitteeMember}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CommitteeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyAssignments([FromQuery] PageRequest paging)
    {
        var result = await committeeService.GetAssignmentsForMemberAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<CommitteeDto>>.Ok(result));
    }

    /// <summary>
    /// Records this member's own decision and the comments behind it, once. When the last
    /// member has voted the committee is complete and the coordinator is told there is a
    /// decision to make.
    /// </summary>
    [HttpPost("api/committees/{committeeId:guid}/review")]
    [Authorize(Roles = $"{RoleNames.InternalCommitteeMember},{RoleNames.ExternalCommitteeMember}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MemberReview(Guid committeeId, [FromBody] CommitteeMemberReviewRequest request)
    {
        await committeeService.MemberReviewAsync(committeeId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    /// <summary>
    /// How many members of each kind a committee needs by default.
    /// </summary>
    [HttpGet("api/settings/default-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDefaultConfig()
    {
        var result = await committeeService.GetDefaultConfigAsync();
        return Ok(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>.Ok(result));
    }

    /// <summary>
    /// Changes that default. It applies to publications opened afterwards: each one keeps the
    /// figures it was opened under.
    /// </summary>
    [HttpPut("api/settings/default-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefaultConfig([FromBody] SetCommitteeRoleConfigRequest request)
    {
        await committeeService.SetDefaultConfigAsync(request);
        return Ok(ApiResponse.Ok("Default committee configuration updated."));
    }

    /// <summary>
    /// The composition one particular committee was built to.
    /// </summary>
    [HttpGet("api/committees/{committeeId:guid}/config")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommitteeConfig(Guid committeeId)
    {
        var result = await committeeService.GetCommitteeConfigAsync(committeeId);
        return Ok(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>.Ok(result));
    }

    /// <summary>
    /// Overrides the composition for one committee, for a paper that genuinely needs a
    /// different panel from the standard one.
    /// </summary>
    [HttpPut("api/committees/{committeeId:guid}/config")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCommitteeConfig(Guid committeeId, [FromBody] SetCommitteeRoleConfigRequest request)
    {
        await committeeService.SetCommitteeConfigAsync(committeeId, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Committee configuration updated."));
    }
}
