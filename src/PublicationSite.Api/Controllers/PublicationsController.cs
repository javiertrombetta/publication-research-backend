using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.DTOs.Publications;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The research paper itself: drafting it, uploading versions of it, submitting it, and the reviews
/// and decisions that follow.
///
/// Versions accumulate rather than replace, because a reviewer's comments refer to the version they
/// read. A paper cannot be submitted until its ethics stage is settled, and an accepted one reaches
/// the public catalogue only if its author says so.
/// </summary>
[ApiController]
[Authorize]
public class PublicationsController(IPublicationService publicationService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Admin-only: puts a version on a paper that is still running, and takes one off. For a file
    /// that arrived by another route, or one uploaded in error. The reason is required and stays
    /// on the publication's history.
    ///
    /// Neither changes the paper's status. Where it should stand afterwards is its own decision,
    /// made through PUT api/containers/{id}/position.
    /// </summary>
    /// <response code="200">The version.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No paper with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/versions/admin")]
    [Authorize(Roles = RoleNames.Admin)]
    [RequestSizeLimit(200_000_000)]
    [ProducesResponseType(typeof(ApiResponse<PublicationVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AdminUploadVersion(Guid publicationId, [FromForm] AdminPaperVersionUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await publicationService.AdminUploadVersionAsync(
            publicationId, currentUser.UserId, stream, form.File.FileName, form.Comments);
        return Ok(ApiResponse<PublicationVersionDto>.Ok(result, "Version added."));
    }

    /// <summary>
    /// Admin-only: removes a version from a paper that is still running, file and all.
    /// </summary>
    /// <response code="200">The version is gone.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No such version on that paper.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpDelete("api/publications/{publicationId:guid}/versions/{versionId:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AdminRemoveVersion(Guid publicationId, Guid versionId, [FromBody] CommentsRequest request)
    {
        await publicationService.AdminRemoveVersionAsync(publicationId, currentUser.UserId, versionId, request.Comments);
        return Ok(ApiResponse<object>.Ok(null!, "Version removed."));
    }

    /// <summary>
    /// Opens the student's research paper on this publication, or hands back the one already
    /// open. There is one paper per publication, so asking twice is safe.
    /// </summary>
    /// <response code="200">The publication.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/publications")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetOrCreateDraft(Guid containerId)
    {
        var result = await publicationService.GetOrCreateDraftAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    /// <summary>
    /// The research paper on this publication, if it has reached that stage.
    /// </summary>
    /// <response code="200">The publication.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No publication with that id.</response>
    [HttpGet("api/containers/{containerId:guid}/publications")]
    [ProducesResponseType(typeof(ApiResponse<PublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByContainer(Guid containerId)
    {
        var result = await publicationService.GetByContainerAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    /// <summary>
    /// One research paper, for the people it concerns: its student, their supervisor and
    /// coordinator, the head of that department, its committee, and an administrator.
    /// </summary>
    /// <response code="200">The publication.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No publication with that id.</response>
    /// <summary>
    /// The papers of several publications at once, each with what its committee said.
    /// </summary>
    /// <remarks>
    /// For the coordinator's decision queue, which asked for the paper and then for its reviews
    /// once per row. As with the ethics equivalent, an id the caller may not read is left out
    /// rather than refusing the request.
    /// </remarks>
    /// <response code="200">One entry per publication whose paper this caller may read.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("api/containers/publications")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ContainerPaperDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPapersFor([FromQuery] Guid[] ids, CancellationToken cancellationToken)
    {
        var result = await publicationService.GetPapersForAsync(ids, currentUser.UserId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ContainerPaperDto>>.Ok(result));
    }

    /// <summary>
    /// One research paper, by its own id rather than by the publication it belongs to.
    ///
    /// For a caller holding a paper id and nothing else, which is where a review, a version and a
    /// committee decision all point. Who may read it is the same question as who may read the
    /// publication it sits on.
    /// </summary>
    /// <response code="200">The research paper.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see.</response>
    /// <response code="404">No research paper with that id.</response>
    [HttpGet("api/publications/{publicationId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid publicationId)
    {
        var result = await publicationService.GetByIdAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    /// <summary>
    /// Edits the paper's title, abstract and keywords while it is still a draft. Once submitted
    /// it is what the reviewers are reading, so it stops being editable.
    /// </summary>
    /// <response code="200">The publication.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("api/publications/{publicationId:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<PublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateMetadata(Guid publicationId, [FromBody] UpdatePublicationMetadataRequest request)
    {
        var result = await publicationService.UpdateMetadataAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse<PublicationDto>.Ok(result));
    }

    /// <summary>
    /// Uploads a new version of the paper. Earlier versions are kept rather than replaced, because
    /// a reviewer's comments refer to the version they read and that has to remain retrievable.
    /// </summary>
    /// <response code="200">The publication version.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/versions")]
    [Authorize(Roles = RoleNames.Student)]
    [RequestSizeLimit(200_000_000)]
    [ProducesResponseType(typeof(ApiResponse<PublicationVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
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

    /// <summary>
    /// Every version uploaded, newest first, with who uploaded it and when.
    /// </summary>
    /// <response code="200">The matching publication versions, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No publication with that id.</response>
    [HttpGet("api/publications/{publicationId:guid}/versions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PublicationVersionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersions(Guid publicationId, [FromQuery] SortRequest sort)
    {
        var result = await publicationService.GetVersionsAsync(publicationId, currentUser.UserId, sort);
        return Ok(ApiResponse<IReadOnlyList<PublicationVersionDto>>.Ok(result));
    }

    /// <summary>
    /// Sends the paper to the supervisor. It requires an uploaded version and a settled ethics
    /// stage: submitting research whose ethics is unresolved is exactly what the workflow
    /// exists to prevent.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/submit")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(Guid publicationId)
    {
        await publicationService.SubmitAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse.Ok("Research paper submitted."));
    }

    /// <summary>
    /// The papers waiting on this supervisor's review, one page at a time.
    /// </summary>
    /// <response code="200">One page of publications, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/publications/pending")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PublicationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingForSupervisor([FromQuery] PageRequest paging, [FromQuery] string? search = null)
    {
        var result = await publicationService.GetPendingForSupervisorAsync(currentUser.UserId, paging, search);
        return Ok(ApiResponse<PagedResult<PublicationDto>>.Ok(result));
    }

    /// <summary>
    /// The papers a supervisor has accepted that still have no evaluation committee, which is the
    /// administrator's queue. Answered in one request, because reconstructing it from the container
    /// listing missed the supervisor's approval and offered papers that would then be refused.
    /// </summary>
    /// <remarks>
    /// Ordered longest-waiting first, or by <c>title</c>, <c>student</c> or <c>waiting</c>. The
    /// search term covers the paper's title and its author.
    /// </remarks>
    /// <response code="200">One page of papers, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/publications/awaiting-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AwaitingCommitteeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAwaitingCommittee([FromQuery] PageRequest paging, [FromQuery] string? search = null)
    {
        var result = await publicationService.GetAwaitingCommitteeAsync(paging, search);
        return Ok(ApiResponse<PagedResult<AwaitingCommitteeDto>>.Ok(result));
    }

    /// <summary>
    /// The supervisor's verdict on the submitted paper. Accepting sends it on for a committee;
    /// sending it back returns it to the student with the reason, and the version they were
    /// reading stays on record.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/supervisor-review")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SupervisorReview(Guid publicationId, [FromBody] PaperReviewDecisionRequest request)
    {
        await publicationService.SupervisorReviewAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    /// <summary>
    /// The file itself, for anyone who can see the publication. Distinct from the catalogue's
    /// download, which serves published papers to readers. This serves unpublished ones to the
    /// people who have to judge them.
    /// </summary>
    /// <response code="200">The file itself, as an attachment.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No publication version with that id.</response>
    [HttpGet("api/publications/{publicationId:guid}/versions/{versionId:guid}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadVersion(Guid publicationId, Guid versionId)
    {
        var (content, fileName) = await publicationService.DownloadVersionAsync(publicationId, versionId, currentUser.UserId);
        return File(content, "application/octet-stream", fileName);
    }

    /// <summary>
    /// Every review recorded against this paper, with the comments behind each one: the
    /// supervisor's, each committee member's, and the coordinator's.
    /// </summary>
    /// <response code="200">The matching reviews, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No publication with that id.</response>
    [HttpGet("api/publications/{publicationId:guid}/reviews")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReviewDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviews(Guid publicationId, [FromQuery] SortRequest sort)
    {
        var result = await publicationService.GetReviewsAsync(publicationId, currentUser.UserId, sort);
        return Ok(ApiResponse<IReadOnlyList<ReviewDto>>.Ok(result));
    }

    /// <summary>
    /// The coordinator's decision on the paper, once the committee has reported. This is the
    /// outcome for the student.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/coordinator-final-decision")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CoordinatorFinalDecision(Guid publicationId, [FromBody] PaperReviewDecisionRequest request)
    {
        await publicationService.CoordinatorFinalDecisionAsync(publicationId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Final decision recorded."));
    }

    /// <summary>
    /// Puts an accepted paper into the catalogue, and says whether the full text is public or
    /// the record alone.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/publications/{publicationId:guid}/publish")]
    [Authorize(Roles = $"{RoleNames.Student},{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PublishDecision(Guid publicationId, [FromBody] PublishDecisionRequest request)
    {
        await publicationService.PublishDecisionAsync(
            publicationId, currentUser.UserId, request, User.IsInRole(RoleNames.Admin));
        return Ok(ApiResponse.Ok("Publication decision recorded."));
    }

    /// <summary>
    /// Withdraws a paper from the catalogue with a recorded reason. The paper and its outcome
    /// remain; only its public listing goes.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication with that id.</response>
    [HttpPost("api/publications/{publicationId:guid}/unpublish")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePublished(Guid publicationId, [FromBody] CommentsRequest request)
    {
        await publicationService.RemovePublishedAsync(publicationId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Publication removed from the public catalogue."));
    }
}
