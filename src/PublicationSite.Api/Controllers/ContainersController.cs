using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
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
    public async Task<IActionResult> Create()
    {
        var result = await containerService.CreateAsync(currentUser.UserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<PublicationContainerDto>.Ok(result));
    }

    [HttpGet("me")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> GetMine()
    {
        var result = await containerService.GetMineAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<PublicationContainerDto>>.Ok(result));
    }

    [HttpGet("supervising")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> GetSupervising()
    {
        var result = await containerService.GetSupervisingAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<PublicationContainerDto>>.Ok(result));
    }

    [HttpGet("in-my-department")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    public async Task<IActionResult> GetInMyDepartment()
    {
        var result = await containerService.GetInMyDepartmentAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<PublicationContainerDto>>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> DeleteOwn(Guid id)
    {
        await containerService.DeleteOwnAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Publication deleted."));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await containerService.GetByIdAsync(id, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }

    [HttpGet("{id:guid}/activity-history")]
    public async Task<IActionResult> GetActivityHistory(Guid id)
    {
        var result = await containerService.GetActivityHistoryAsync(id, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ActivityHistoryEntryDto>>.Ok(result));
    }

    [HttpGet]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? studentId, [FromQuery] Guid? coordinatorId, [FromQuery] string? status)
    {
        var result = await containerService.GetAllAsync(studentId, coordinatorId, status);
        return Ok(ApiResponse<IReadOnlyList<PublicationContainerDto>>.Ok(result));
    }

    [HttpPost("assign-coordinator")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> AssignCoordinator([FromBody] AssignCoordinatorRequest request)
    {
        var result = await containerService.AssignCoordinatorManuallyAsync(request, currentUser.UserId);
        return Ok(ApiResponse<PublicationContainerDto>.Ok(result));
    }
}
