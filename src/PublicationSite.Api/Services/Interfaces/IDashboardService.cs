using PublicationSite.Api.DTOs.Dashboard;

namespace PublicationSite.Api.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
