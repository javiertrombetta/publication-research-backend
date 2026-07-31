using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The published catalogue. Anonymous per action rather than for the whole controller:
/// [AllowAnonymous] at class level short-circuits authorisation for every action in it, which
/// would leave the full-text download open to the public no matter what attribute it carried.
/// </summary>
[ApiController]
[Route("api/catalogue")]
public class CatalogueController(ICatalogueService catalogueService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search([FromQuery] CatalogueSearchRequest request)
    {
        var result = await catalogueService.SearchAsync(request);
        return Ok(ApiResponse<PagedResult<CatalogueEntryDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await catalogueService.GetByIdAsync(id);
        return Ok(ApiResponse<CatalogueEntryDto>.Ok(result));
    }

    /// <summary>
    /// The full text is NOT public: a reader without an account asks the institution for a copy
    /// instead. Signed-in users keep direct access.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [Authorize]
    public async Task<IActionResult> Download(Guid id)
    {
        var (content, fileName) = await catalogueService.DownloadAsync(id);
        return File(content, "application/pdf", fileName);
    }

    [HttpGet("{id:guid}/citation")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCitation(Guid id)
    {
        var result = await catalogueService.GetCitationAsync(id);
        return Ok(ApiResponse<CitationDto>.Ok(result));
    }
}
