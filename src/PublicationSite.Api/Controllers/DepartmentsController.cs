using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Departments;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Departments, and which coordinators are available in them. Reading the list is open without a
/// session because the sign-up form needs it before anybody has an account; changing one is an
/// administrator's.
/// </summary>
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
    /// <response code="200">The matching departments, all of them.</response>
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
    /// <response code="200">The department.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="404">No department with that id.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await departmentService.GetByIdAsync(id);
        return Ok(ApiResponse<DepartmentDto>.Ok(result));
    }

    /// <summary>
    /// Adds a department. Its code is what appears against a student, so it is expected to be
    /// short and is required to be unique.
    /// </summary>
    /// <response code="201">The department was created. Its id is in the body, and the Location header points at it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="409">It is already recorded — this has been done, or created, before.</response>
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request)
    {
        var result = await departmentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DepartmentDto>.Ok(result));
    }

    /// <summary>
    /// Renames a department or corrects its code. Nothing that already belongs to it is moved.
    /// </summary>
    /// <response code="200">The department.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No department with that id.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request)
    {
        var result = await departmentService.UpdateAsync(id, request);
        return Ok(ApiResponse<DepartmentDto>.Ok(result));
    }

    /// <summary>
    /// Removes a department, provided nobody belongs to it. Students, supervisors and
    /// coordinators reference it, so one still in use is refused rather than emptied.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No department with that id.</response>
    /// <response code="409">It is already recorded — this has been done, or created, before.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
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
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No coordinator profile with that id.</response>
    [HttpPut("coordinators/{coordinatorUserId:guid}/availability")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCoordinatorAvailability(Guid coordinatorUserId, [FromQuery] bool isAvailable)
    {
        await departmentService.SetCoordinatorAvailabilityAsync(coordinatorUserId, isAvailable);
        return Ok(ApiResponse.Ok("Coordinator availability updated."));
    }
}
