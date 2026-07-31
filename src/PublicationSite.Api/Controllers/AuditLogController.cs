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
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] AuditLogQuery query)
    {
        var result = await auditLogQueryService.GetAsync(query);
        return Ok(ApiResponse<PagedResult<AuditLogEntryDto>>.Ok(result));
    }

    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query)
    {
        var csv = await auditLogQueryService.ExportCsvAsync(query);
        return File(csv, "text/csv", $"audit-log-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
