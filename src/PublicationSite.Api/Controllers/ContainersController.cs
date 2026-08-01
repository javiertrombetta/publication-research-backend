using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/containers")]
[Authorize]
public class ContainersController(IContainerService containerService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Starts a publication. A student may run several at once, each with its own proposals,
    /// ethics workflow and paper, so there is no cap.
    /// </summary>
    /// <remarks>
    /// A coordinator is allocated automatically by department workload, and the committee
    /// composition in force today is recorded on it — a publication runs for months, and an
    /// administrator changing the rules in March must not change them for research started in
    /// January.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create()
    {
        var result = await containerService.CreateAsync(currentUser.UserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<PublicationContainerDto>.Ok(result));
    }

    /// <summary>
    /// This student's own publications, newest first, one page at a time. Each says what stage
    /// it is at and whose turn it is, so the list can show that without a request per row.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine([FromQuery] PageRequest paging)
    {
        var result = await containerService.GetMineAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// The publications this supervisor was allocated.
    /// </summary>
    [HttpGet("supervising")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupervising([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetSupervisingAsync(currentUser.UserId, query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// The publications of students in the department this person heads — and only that
    /// department's.
    /// </summary>
    [HttpGet("in-my-department")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInMyDepartment([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetInMyDepartmentAsync(currentUser.UserId, query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// Discards one of the student's own publications, and only while it still holds no
    /// proposals. Once anybody has been asked to look at it, it stays.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteOwn(Guid id)
    {
        await containerService.DeleteOwnAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Publication deleted."));
    }

    /// <summary>
    /// One publication. Readable by the people it concerns: its student, their coordinator and
    /// supervisor, the head of that department, its evaluation committee, and an administrator.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await containerService.GetByIdAsync(id, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }

    /// <summary>
    /// Everything that has happened to this publication, newest first — who did it, in what
    /// capacity, and the comment that justified it. This is what makes a decision explicable
    /// months later.
    /// </summary>
    [HttpGet("{id:guid}/activity-history")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ActivityHistoryEntryDto>>), StatusCodes.Status200OK)]
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
    [HttpGet]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetAllAsync(query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    /// <summary>
    /// Moves a publication to a different coordinator, or opens one for a student on their
    /// behalf. For when the automatic allocation got it wrong or the person it chose has gone.
    /// </summary>
    [HttpPost("assign-coordinator")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignCoordinator([FromBody] AssignCoordinatorRequest request)
    {
        var result = await containerService.AssignCoordinatorManuallyAsync(request, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }
}
