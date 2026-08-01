using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Publications: the container a student's research runs inside, holding its proposals, its ethics
/// workflow and its paper. A student may have several at once, each at its own stage.
///
/// Opening one allocates a coordinator by departmental workload and records the committee rules in
/// force that day. What can be read of one depends on who is asking: its student, their supervisor
/// and coordinator, the head of that department, its committee, or an administrator.
/// </summary>
[ApiController]
[Route("api/containers")]
[Authorize]
public class ContainersController(IContainerService containerService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Starts a publication. A student may run several at once, each with its own proposals, ethics
    /// workflow and paper, so there is no cap.
    /// </summary>
    /// <remarks>
    /// A coordinator is allocated automatically by department workload, and the committee
    /// composition in force today is recorded on it. A publication runs for months, and an
    /// administrator changing the rules in March must not change them for research started in
    /// January.
    /// </remarks>
    /// <response code="201">The publication was opened. Its id is in the body, and the Location header points at it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create()
    {
        var result = await containerService.CreateAsync(currentUser.UserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<PublicationContainerDto>.Ok(result));
    }

    /// <summary>
    /// This student's own publications, newest first, one page at a time. Each says what stage
    /// it is at and whose turn it is, so the list can show that without a request per row.
    /// </summary>
    /// <response code="200">One page of publications, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("me")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine([FromQuery] PageRequest paging)
    {
        var result = await containerService.GetMineAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// The publications this supervisor was allocated.
    /// </summary>
    /// <response code="200">One page of publications, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("supervising")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSupervising([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetSupervisingAsync(currentUser.UserId, query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// The publications of students in the department this person heads, and only that
    /// department's.
    /// </summary>
    /// <response code="200">One page of publications, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("in-my-department")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInMyDepartment([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetInMyDepartmentAsync(currentUser.UserId, query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// Discards one of the student's own publications, and only while it still holds no
    /// proposals. Once anybody has been asked to look at it, it stays.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteOwn(Guid id)
    {
        await containerService.DeleteOwnAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Publication deleted."));
    }

    /// <summary>
    /// One publication. Readable by the people it concerns: its student, their coordinator and
    /// supervisor, the head of that department, its evaluation committee, and an administrator.
    /// </summary>
    /// <response code="200">The publication.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await containerService.GetByIdAsync(id, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }

    /// <summary>
    /// Everything that has happened to this publication, newest first: who did it, in what
    /// capacity, and the comment that justified it. This is what makes a decision explicable months
    /// later.
    /// </summary>
    /// <response code="200">The matching activity history entrys, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    [HttpGet("{id:guid}/activity-history")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ActivityHistoryEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActivityHistory(Guid id)
    {
        var result = await containerService.GetActivityHistoryAsync(id, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ActivityHistoryEntryDto>>.Ok(result));
    }

    /// <summary>
    /// Publications across the institution, filtered by student, coordinator, status or which
    /// ethics decision they are waiting at, one page at a time. A coordinator passes their own
    /// id, since the whole institution is not their queue.
    /// </summary>
    /// <response code="200">One page of publications, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetAllAsync(query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// Moves a publication to a different coordinator, or opens one for a student on their
    /// behalf. For when the automatic allocation got it wrong or the person it chose has gone.
    /// </summary>
    /// <response code="200">The publication.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("assign-coordinator")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignCoordinator([FromBody] AssignCoordinatorRequest request)
    {
        var result = await containerService.AssignCoordinatorManuallyAsync(request, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }
}
