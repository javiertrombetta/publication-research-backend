using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.AuditLog;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The trail. Every consequential act in the system writes a line here: who did it, in what
/// capacity, to what, and the comment that justified it. Nothing removes one. It is what makes a
/// decision explicable months later, and it is why accounts are anonymised rather than deleted.
/// Administrators only.
/// </summary>
[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = RoleNames.Admin)]
public class AuditLogController(IAuditLogQueryService auditLogQueryService) : ControllerBase
{
    /// <summary>
    /// Every recorded action, newest first, filtered and paged. The trail is append-only and
    /// nothing in the application deletes from it, so this is the record of who did what to
    /// whose work.
    /// </summary>
    /// <response code="200">One page of trail entries, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] AuditLogQuery query)
    {
        var result = await auditLogQueryService.GetAsync(query);
        return Ok(ApiResponse<PagedResult<AuditLogEntryDto>>.Ok(result));
    }

    /// <summary>
    /// The same trail as a CSV file, for handing to somebody who is not going to be given an
    /// account, such as an auditor or a committee asking how a decision was reached.
    /// </summary>
    /// <response code="200">The file itself, as an attachment.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query)
    {
        var csv = await auditLogQueryService.ExportCsvAsync(query);
        return File(csv, "text/csv", $"audit-log-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
