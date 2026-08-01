using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.AuditLog;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Controllers;

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
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] AuditLogQuery query)
    {
        var result = await auditLogQueryService.GetAsync(query);
        return Ok(ApiResponse<PagedResult<AuditLogEntryDto>>.Ok(result));
    }

    /// <summary>
    /// The same trail as a CSV file, for handing to somebody who is not going to be given an
    /// account — an auditor, or a committee asking how a decision was reached.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query)
    {
        var csv = await auditLogQueryService.ExportCsvAsync(query);
        return File(csv, "text/csv", $"audit-log-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
