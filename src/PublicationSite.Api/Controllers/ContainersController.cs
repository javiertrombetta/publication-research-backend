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
    [HttpPost]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create()
    {
        var result = await containerService.CreateAsync(currentUser.UserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<PublicationContainerDto>.Ok(result));
    }

    [HttpGet("me")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine([FromQuery] PageRequest paging)
    {
        var result = await containerService.GetMineAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    [HttpGet("supervising")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupervising([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetSupervisingAsync(currentUser.UserId, query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    [HttpGet("in-my-department")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInMyDepartment([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetInMyDepartmentAsync(currentUser.UserId, query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteOwn(Guid id)
    {
        await containerService.DeleteOwnAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Publication deleted."));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await containerService.GetByIdAsync(id, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }

    [HttpGet("{id:guid}/activity-history")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ActivityHistoryEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityHistory(Guid id)
    {
        var result = await containerService.GetActivityHistoryAsync(id, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ActivityHistoryEntryDto>>.Ok(result));
    }

    [HttpGet]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationContainerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] ContainerQuery query)
    {
        var result = await containerService.GetAllAsync(query);
        return Ok(ApiResponse<PagedResult<PublicationContainerDto>>.Ok(result));
    }

    [HttpPost("assign-coordinator")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PublicationContainerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignCoordinator([FromBody] AssignCoordinatorRequest request)
    {
        var result = await containerService.AssignCoordinatorManuallyAsync(request, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }
}
