using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Departments;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController(IDepartmentService departmentService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var result = await departmentService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<DepartmentDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await departmentService.GetByIdAsync(id);
        return Ok(ApiResponse<DepartmentDto>.Ok(result));
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        var result = await departmentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DepartmentDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request)
    {
        var result = await departmentService.UpdateAsync(id, request);
        return Ok(ApiResponse<DepartmentDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await departmentService.DeleteAsync(id);
        return Ok(ApiResponse.Ok("Department deleted."));
    }

    [HttpPut("coordinators/{coordinatorUserId:guid}/availability")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> SetCoordinatorAvailability(Guid coordinatorUserId, [FromQuery] bool isAvailable)
    {
        await departmentService.SetCoordinatorAvailabilityAsync(coordinatorUserId, isAvailable);
        return Ok(ApiResponse.Ok("Coordinator availability updated."));
    }
}
