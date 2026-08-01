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
    /// <summary>
    /// Every department, each with the person heading it. Readable by any signed-in user
    /// because a department is not sensitive and half the forms in the system ask somebody to
    /// pick one.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DepartmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var result = await departmentService.GetAllAsync();
        return Ok(ApiResponse<IReadOnlyList<DepartmentDto>>.Ok(result));
    }

    /// <summary>
    /// One department, for a screen that is already about it.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await departmentService.GetByIdAsync(id);
        return Ok(ApiResponse<DepartmentDto>.Ok(result));
    }

    /// <summary>
    /// Adds a department. Its code is what appears against a student, so it is expected to be
    /// short and is required to be unique.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        var result = await departmentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DepartmentDto>.Ok(result));
    }

    /// <summary>
    /// Renames a department or corrects its code. Nothing that already belongs to it is moved.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request)
    {
        var result = await departmentService.UpdateAsync(id, request);
        return Ok(ApiResponse<DepartmentDto>.Ok(result));
    }

    /// <summary>
    /// Removes a department, provided nobody belongs to it. Students, supervisors and
    /// coordinators reference it, so one still in use is refused rather than emptied.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await departmentService.DeleteAsync(id);
        return Ok(ApiResponse.Ok("Department deleted."));
    }

    /// <summary>
    /// Takes a coordinator in or out of the automatic allocation that runs when a student
    /// starts a publication.
    /// </summary>
    /// <remarks>
    /// For leave and workload rather than for discipline: an unavailable coordinator keeps
    /// their account, their publications and their say over them, and simply stops being handed
    /// new ones.
    /// </remarks>
    [HttpPut("coordinators/{coordinatorUserId:guid}/availability")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCoordinatorAvailability(Guid coordinatorUserId, [FromQuery] bool isAvailable)
    {
        await departmentService.SetCoordinatorAvailabilityAsync(coordinatorUserId, isAvailable);
        return Ok(ApiResponse.Ok("Coordinator availability updated."));
    }
}
