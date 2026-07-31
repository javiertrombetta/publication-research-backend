using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Authorize]
public class PublicationsController(IPublicationService publicationService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("api/containers/{containerId:guid}/publications")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> GetOrCreateDraft(Guid containerId)
    {
        var result = await publicationService.GetOrCreateDraftAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    [HttpGet("api/containers/{containerId:guid}/publications")]
    public async Task<IActionResult> GetByContainer(Guid containerId)
    {
        var result = await publicationService.GetByContainerAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    [HttpGet("api/publications/{publicationId:guid}")]
    public async Task<IActionResult> GetById(Guid publicationId)
    {
        var result = await publicationService.GetByIdAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    [HttpPut("api/publications/{publicationId:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> UpdateMetadata(Guid publicationId, [FromBody] UpdatePublicationMetadataRequest request)
    {
        var result = await publicationService.UpdateMetadataAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    [HttpPost("api/publications/{publicationId:guid}/versions")]
    [Authorize(Roles = RoleNames.Student)]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> UploadVersion(Guid publicationId, [FromForm] PublicationVersionUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        Stream? supplementaryStream = null;
        try
        {
            supplementaryStream = form.SupplementaryFile?.OpenReadStream();
            var result = await publicationService.UploadVersionAsync(publicationId, currentUser.UserId, stream, form.File.FileName,
                supplementaryStream, form.SupplementaryFile?.FileName, form.ReviewerNotes);
            return Ok(ApiResponse<PublicationVersionDto>.Ok(result));
        }
        finally
        {
            if (supplementaryStream is not null)
            {
                await supplementaryStream.DisposeAsync();
            }
        }
    }

    [HttpGet("api/publications/{publicationId:guid}/versions")]
    public async Task<IActionResult> GetVersions(Guid publicationId)
    {
        var result = await publicationService.GetVersionsAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<PublicationVersionDto>>.Ok(result));
    }

    [HttpPost("api/publications/{publicationId:guid}/submit")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Submit(Guid publicationId)
    {
        await publicationService.SubmitAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse.Ok("Research paper submitted."));
    }

    [HttpGet("api/publications/pending")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> GetPendingForSupervisor()
    {
        var result = await publicationService.GetPendingForSupervisorAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<PublicationDto>>.Ok(result));
    }

    [HttpPost("api/publications/{publicationId:guid}/supervisor-review")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> SupervisorReview(Guid publicationId, [FromBody] PaperReviewDecisionRequest request)
    {
        await publicationService.SupervisorReviewAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    [HttpGet("api/publications/{publicationId:guid}/reviews")]
    public async Task<IActionResult> GetReviews(Guid publicationId)
    {
        var result = await publicationService.GetReviewsAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ReviewDto>>.Ok(result));
    }

    [HttpPost("api/publications/{publicationId:guid}/coordinator-final-decision")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> CoordinatorFinalDecision(Guid publicationId, [FromBody] PaperReviewDecisionRequest request)
    {
        await publicationService.CoordinatorFinalDecisionAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Final decision recorded."));
    }

    [HttpPost("api/publications/{publicationId:guid}/publish")]
    [Authorize(Roles = $"{RoleNames.Student},{RoleNames.Admin},{RoleNames.Coordinator}")]
    public async Task<IActionResult> PublishDecision(Guid publicationId, [FromBody] PublishDecisionRequest request)
    {
        await publicationService.PublishDecisionAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Publication decision recorded."));
    }

    [HttpPost("api/publications/{publicationId:guid}/unpublish")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> RemovePublished(Guid publicationId, [FromBody] CommentsRequest request)
    {
        await publicationService.RemovePublishedAsync(publicationId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Publication removed from the public catalogue."));
    }
}
