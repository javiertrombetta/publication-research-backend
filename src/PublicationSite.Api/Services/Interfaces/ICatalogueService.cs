using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Services.Interfaces;

public interface ICatalogueService
{
    Task<PagedResult<CatalogueEntryDto>> SearchAsync(CatalogueSearchRequest request, CancellationToken cancellationToken = default);
    Task<CatalogueEntryDto> GetByIdAsync(Guid publicationId, CancellationToken cancellationToken = default);
    Task<(Stream Content, string FileName)> DownloadAsync(Guid publicationId, CancellationToken cancellationToken = default);
    Task<CitationDto> GetCitationAsync(Guid publicationId, CancellationToken cancellationToken = default);
}
