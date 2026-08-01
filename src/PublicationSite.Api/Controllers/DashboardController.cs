using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Dashboard;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

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
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var result = await dashboardService.GetSummaryAsync();
        return Ok(ApiResponse<DashboardSummaryDto>.Ok(result));
    }
}
