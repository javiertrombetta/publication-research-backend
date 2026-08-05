using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Filters;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The published catalogue: the accepted research whose authors chose to make it public, and the
/// only part of this system anybody can read without an account.
///
/// A record being listed and its full text being readable are separate decisions, so the search
/// and the detail are open while the download is not always. An administrator can also switch the
/// whole catalogue off for a deployment that is not ready to have one, and then none of this
/// answers a visitor who is not signed in.
/// </summary>
// Anonymous is granted per action rather than on the class: [AllowAnonymous] at class level
// short-circuits authorisation for every action underneath it, which would leave the full-text
// download open to the public no matter what attribute it carried.
[ApiController]
[Route("api/catalogue")]
[PublicCatalogueRequired]
public class CatalogueController(ICatalogueService catalogueService) : ControllerBase
{
    /// <summary>
    /// The public catalogue: published research, searchable by title, abstract, author,
    /// supervisor, year, keyword, type, department and research area.
    /// </summary>
    /// <remarks>
    /// Only papers whose author chose to publish them appear, and an administrator can switch
    /// the whole catalogue off for a deployment that is not ready to have one.
    /// </remarks>
    /// <response code="200">One page of catalogue entries, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="404">Not available: the public catalogue is switched off for this deployment.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CatalogueEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Search([FromQuery] CatalogueSearchRequest request)
    {
        var result = await catalogueService.SearchAsync(request);
        return Ok(ApiResponse<PagedResult<CatalogueEntryDto>>.Ok(result));
    }

    /// <summary>
    /// One published paper in full, for its own page.
    /// </summary>
    /// <response code="200">The published paper, in full.</response>
    /// <response code="404">No publication with that id.</response>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CatalogueEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await catalogueService.GetByIdAsync(id);
        return Ok(ApiResponse<CatalogueEntryDto>.Ok(result));
    }

    /// <summary>
    /// The full text is NOT public: a reader without an account asks the institution for a copy
    /// instead. Signed-in users keep direct access.
    /// </summary>
    /// <response code="200">The file itself, as an attachment.</response>
    /// <response code="404">Neither the publication nor the publication version was found by that id.</response>
    [HttpGet("{id:guid}/download")]
    [Authorize]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id)
    {
        var (content, fileName) = await catalogueService.DownloadAsync(id);
        return File(content, "application/pdf", fileName);
    }

    /// <summary>
    /// The paper's citation, formatted ready to paste. Composed here rather than in each client
    /// so two readers quoting the same work quote it identically.
    /// </summary>
    /// <response code="200">The citation.</response>
    /// <response code="404">No publication with that id.</response>
    [HttpGet("{id:guid}/citation")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCitation(Guid id)
    {
        var result = await catalogueService.GetCitationAsync(id);
        return Ok(ApiResponse<CitationDto>.Ok(result));
    }
}
