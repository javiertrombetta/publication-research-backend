using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/catalogue")]
[AllowAnonymous]
public class CatalogueController(ICatalogueService catalogueService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] CatalogueSearchRequest request)
    {
        var result = await catalogueService.SearchAsync(request);
        return Ok(ApiResponse<PagedResult<CatalogueEntryDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await catalogueService.GetByIdAsync(id);
        return Ok(ApiResponse<CatalogueEntryDto>.Ok(result));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var (content, fileName) = await catalogueService.DownloadAsync(id);
        return File(content, "application/pdf", fileName);
    }

    [HttpGet("{id:guid}/citation")]
    public async Task<IActionResult> GetCitation(Guid id)
    {
        var result = await catalogueService.GetCitationAsync(id);
        return Ok(ApiResponse<CitationDto>.Ok(result));
    }
}
