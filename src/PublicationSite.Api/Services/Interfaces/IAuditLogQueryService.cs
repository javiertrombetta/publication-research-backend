using PublicationSite.Api.DTOs.AuditLog;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Services.Interfaces;

public interface IAuditLogQueryService
{
    Task<PagedResult<AuditLogEntryDto>> GetAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<byte[]> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}
