using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.AuditLog;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/audit-log")]
[Authorize(Roles = RoleNames.Admin)]
public class AuditLogController(IAuditLogQueryService auditLogQueryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] AuditLogQuery query)
    {
        var result = await auditLogQueryService.GetAsync(query);
        return Ok(ApiResponse<PagedResult<AuditLogEntryDto>>.Ok(result));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AuditLogQuery query)
    {
        var csv = await auditLogQueryService.ExportCsvAsync(query);
        return File(csv, "text/csv", $"audit-log-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}
