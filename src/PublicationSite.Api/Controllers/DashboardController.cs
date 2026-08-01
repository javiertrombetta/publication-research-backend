using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Dashboard;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The institution-wide picture an administrator lands on: how many accounts, publications and
/// papers there are, and where they have got to.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = RoleNames.Admin)]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    /// <summary>
    /// The institution's research activity in figures: how many publications exist and where
    /// they have got to, how many papers are published, how the ethics approvals are
    /// distributed, and how much reviewing is outstanding.
    /// </summary>
    /// <remarks>
    /// Counts rather than rows, so the numbers stay right however large the institution grows.
    /// </remarks>
    /// <response code="200">The dashboard summary.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary()
    {
        var result = await dashboardService.GetSummaryAsync();
        return Ok(ApiResponse<DashboardSummaryDto>.Ok(result));
    }
}
