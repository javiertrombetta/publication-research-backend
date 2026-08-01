using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// A coordinator's saved sets of supervisors.
///
/// Sending proposals out means ticking the same people over and over, cycle after cycle, and the
/// list is usually the same one: a research area, a pair who co-supervise, the two who take on
/// quantitative work. Naming that list once and picking it by name afterwards is quicker and
/// harder to get wrong than rebuilding it by hand.
///
/// Groups are personal. Each coordinator sees only their own, and a group grants nothing: it fills
/// in the form, and the send itself is checked exactly as it would be if the names had been ticked
/// one at a time.
///
/// Administrators get a view across all of them, and can rename, re-crew or discard any. Personal
/// lists accumulate: a coordinator who leaves takes nobody's groups with them, and a group naming
/// three people who no longer supervise is worse than no group at all, so somebody has to be able
/// to clear them out.
/// </summary>
[ApiController]
[Authorize]
public class SupervisorGroupsController(
    ISupervisorGroupService groupService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// The signed-in coordinator's groups, in alphabetical order, each with its members.
    /// </summary>
    /// <response code="200">The groups. An empty list where none have been made yet.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    [HttpGet("api/supervisor-groups")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SupervisorGroupDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var groups = await groupService.GetMineAsync(currentUser.UserId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupervisorGroupDto>>.Ok(groups));
    }

    /// <summary>
    /// Saves a new group under a name of the coordinator's choosing.
    /// </summary>
    /// <response code="200">The group as saved.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="409">A group of the coordinator's already goes by that name.</response>
    /// <response code="422">Understood, and refused: somebody chosen is not a supervisor, or no longer has an account.</response>
    [HttpPost("api/supervisor-groups")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<SupervisorGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] SaveSupervisorGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await groupService.CreateAsync(currentUser.UserId, request, cancellationToken);
        return Ok(ApiResponse<SupervisorGroupDto>.Ok(group, "Group saved."));
    }

    /// <summary>
    /// Renames a group, changes who is in it, or both. Membership is replaced by what the request
    /// carries rather than added to.
    /// </summary>
    /// <response code="200">The group as saved.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No group of the coordinator's with that id.</response>
    /// <response code="409">Another group of theirs already goes by that name.</response>
    /// <response code="422">Understood, and refused: somebody chosen is not a supervisor, or no longer has an account.</response>
    [HttpPut("api/supervisor-groups/{groupId:guid}")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<SupervisorGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid groupId, [FromBody] SaveSupervisorGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await groupService.UpdateAsync(groupId, currentUser.UserId, request, cancellationToken);
        return Ok(ApiResponse<SupervisorGroupDto>.Ok(group, "Group saved."));
    }

    /// <summary>
    /// Discards a group. Nothing else goes with it: proposals already sent to its members stay
    /// exactly as they were, because a group only ever filled in a form.
    /// </summary>
    /// <response code="200">The group is gone.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No group of the coordinator's with that id.</response>
    [HttpDelete("api/supervisor-groups/{groupId:guid}")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid groupId, CancellationToken cancellationToken)
    {
        await groupService.DeleteAsync(groupId, currentUser.UserId, cancellationToken);
        return Ok(ApiResponse.Ok("Group deleted."));
    }

    // ---------- The administrator's view across everybody's ----------

    /// <summary>
    /// Every group in the institution, ordered by the coordinator who owns it. Narrowed by the
    /// search term, which matches a group's name, its owner's name or any member's name.
    /// </summary>
    /// <response code="200">The groups. An empty list where none have been made yet.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    [HttpGet("api/supervisor-groups/all")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SupervisorGroupDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var groups = await groupService.GetAllAsync(search, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupervisorGroupDto>>.Ok(groups));
    }

    /// <summary>
    /// Renames a group or changes who is in it, whoever it belongs to. The group stays with its
    /// owner: tidying somebody's list up is not taking it off them.
    /// </summary>
    /// <response code="200">The group as saved.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No group with that id.</response>
    /// <response code="409">Its owner already has another group by that name.</response>
    /// <response code="422">Understood, and refused: somebody chosen is not a supervisor, or no longer has an account.</response>
    [HttpPut("api/supervisor-groups/{groupId:guid}/any")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<SupervisorGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAny(
        Guid groupId, [FromBody] SaveSupervisorGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await groupService.UpdateAsync(groupId, null, request, cancellationToken);
        return Ok(ApiResponse<SupervisorGroupDto>.Ok(group, "Group saved."));
    }

    /// <summary>
    /// Discards the groups named, whoever they belong to.
    /// </summary>
    /// <response code="200">How many were discarded.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    [HttpPost("api/supervisor-groups/delete")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteMany(
        [FromBody] DeleteSupervisorGroupsRequest request, CancellationToken cancellationToken)
    {
        // Two ways in, so clearing the lot is asked for in as many words. An empty list of ids
        // meaning "all of them" is the sort of shorthand that empties a table by accident.
        var deleted = request.All
            ? await groupService.DeleteAllAsync(cancellationToken)
            : await groupService.DeleteManyAsync(request.GroupIds, cancellationToken);

        return Ok(ApiResponse<int>.Ok(deleted,
            deleted == 1 ? "One group deleted." : $"{deleted} groups deleted."));
    }
}
