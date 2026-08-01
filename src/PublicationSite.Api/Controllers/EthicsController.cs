using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The ethics workflow, which is the reason this system exists rather than a shared drive.
///
/// A student declares whether their research needs approval, but does not settle it: the supervisor
/// decides, the coordinator confirms that decision, and only then does anything move. Where
/// approval is required, the documents asked for are recorded at that moment and the student is
/// judged against that list, not against one that changed while they were preparing it. The uploads
/// are then read by the supervisor, the coordinator and the head of department in turn, and the
/// coordinator closes the stage. Nothing reaches the paper stage until it does.
/// </summary>
[ApiController]
[Authorize]
public class EthicsController(IEthicsService ethicsService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// The questions a student should ask themselves before declaring whether their research needs
    /// ethics approval, and what each answer means. Open to anyone, including someone not yet
    /// signed in, because it is guidance rather than a record.
    /// </summary>
    /// <response code="200">The ethics guidance.</response>
    [HttpGet("api/ethics/guidance")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<EthicsGuidanceDto>), StatusCodes.Status200OK)]
    public IActionResult GetGuidance() => Ok(ApiResponse<EthicsGuidanceDto>.Ok(ethicsService.GetGuidance()));
    /// <summary>
    /// The student's own declaration: does this research need ethics approval? An answer of no,
    /// or of unsure, does not settle it. The supervisor decides either way, and the coordinator
    /// confirms that decision, so nobody rules themselves out of ethics review.
    /// </summary>
    /// <response code="200">The ethics declaration.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/declaration")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<EthicsDeclarationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitDeclaration(Guid containerId, [FromBody] EthicsDeclarationRequest request)
    {
        var result = await ethicsService.SubmitDeclarationAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse<EthicsDeclarationDto>.Ok(result));
    }

    /// <summary>
    /// Where the ethics workflow has got to on this publication: the declaration, the
    /// supervisor's decision, which documents were asked for, what has been uploaded and
    /// reviewed, and whose turn it is now.
    /// </summary>
    /// <response code="200">The ethics approval.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No ethics approval with that id.</response>
    [HttpGet("api/containers/{containerId:guid}/ethics")]
    [ProducesResponseType(typeof(ApiResponse<EthicsApprovalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApproval(Guid containerId)
    {
        var result = await ethicsService.GetApprovalAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<EthicsApprovalDto>.Ok(result));
    }

    /// <summary>
    /// The supervisor rules on whether approval is required, and if it is, which documents the
    /// student must produce. That list is recorded here and then held: a student is judged
    /// against what they were asked for, not against a list that changed while they were
    /// preparing it.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the ethics approval nor the publication container was found by that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/supervisor-decision")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SupervisorDecision(Guid containerId, [FromBody] SupervisorRequirementDecisionRequest request)
    {
        await ethicsService.SubmitSupervisorRequirementDecisionAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    /// <summary>
    /// Uploads one of the ethics documents the student was asked for. Uploading again replaces
    /// the earlier attempt as a new version, so a document sent back for correction keeps its
    /// history.
    /// </summary>
    /// <response code="200">The ethics document.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the ethics approval nor the publication container was found by that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/documents")]
    [Authorize(Roles = RoleNames.Student)]
    [RequestSizeLimit(100_000_000)]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadDocument(Guid containerId, [FromForm] EthicsDocumentUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await ethicsService.UploadDocumentAsync(containerId, currentUser.UserId, form.DocumentType, stream, form.File.FileName);
        return Ok(ApiResponse<EthicsDocumentDto>.Ok(result));
    }

    /// <summary>
    /// What this publication has been asked to supply, and what is still outstanding.
    /// </summary>
    /// <response code="200">The matching required ethics documents, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    [HttpGet("api/containers/{containerId:guid}/ethics/required-documents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RequiredEthicsDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRequiredDocuments(Guid containerId)
    {
        var result = await ethicsService.GetRequiredDocumentsAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<RequiredEthicsDocumentDto>>.Ok(result));
    }

    /// <summary>
    /// The ethics documents uploaded so far, with each one's review state and the reviewer's
    /// comments.
    /// </summary>
    /// <response code="200">The matching ethics documents, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    [HttpGet("api/containers/{containerId:guid}/ethics/documents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EthicsDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDocuments(Guid containerId)
    {
        var result = await ethicsService.GetDocumentsAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<EthicsDocumentDto>>.Ok(result));
    }

    /// <summary>
    /// One uploaded document, so the people asked to approve it can read it first.
    /// </summary>
    /// <response code="200">The file itself, as an attachment.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No ethics document with that id.</response>
    [HttpGet("api/containers/{containerId:guid}/ethics/documents/{documentId:guid}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadDocument(Guid containerId, Guid documentId)
    {
        var (content, fileName) = await ethicsService.DownloadDocumentAsync(containerId, documentId, currentUser.UserId);
        return File(content, "application/octet-stream", fileName);
    }

    /// <summary>
    /// The supervisor's verdict on the uploaded documents. Anything sent back returns to the
    /// student with the reason, and the workflow waits rather than advancing on an incomplete
    /// set.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the ethics approval nor the publication container was found by that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/supervisor-review")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SupervisorReview(Guid containerId, [FromBody] DocumentReviewDecisionRequest request)
    {
        await ethicsService.SupervisorReviewDocumentsAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    /// <summary>
    /// The coordinator's confirmation when the supervisor ruled approval is not required.
    /// Agreeing releases the publication to its paper stage; disagreeing puts ethics back in
    /// play, which is the point of asking a second person.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the ethics approval nor the publication container was found by that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-not-required-review")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CoordinatorNotRequiredReview(Guid containerId, [FromBody] CoordinatorNotRequiredReviewRequest request)
    {
        await ethicsService.CoordinatorReviewNotRequiredAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    /// <summary>
    /// The coordinator's review of documents the supervisor has already accepted, before they
    /// go to the head of department.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the ethics approval nor the publication container was found by that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-document-review")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CoordinatorDocumentReview(Guid containerId, [FromBody] CoordinatorDocumentReviewRequest request)
    {
        await ethicsService.CoordinatorReviewDocumentsAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    /// <summary>
    /// The head of department's review, which is the last academic check before the coordinator
    /// closes the ethics stage.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No ethics approval with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/hod-review")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> HeadOfDepartmentReview(Guid containerId, [FromBody] HeadOfDepartmentReviewRequest request)
    {
        await ethicsService.HeadOfDepartmentReviewAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Comments recorded."));
    }

    /// <summary>
    /// Closes the ethics stage. Approval unblocks the paper; refusal stops the publication
    /// there, with the reason recorded against it.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">Neither the ethics approval nor the publication container was found by that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-final-decision")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CoordinatorFinalDecision(Guid containerId, [FromBody] CoordinatorFinalDecisionRequest request)
    {
        await ethicsService.CoordinatorFinalDecisionAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Final decision recorded."));
    }
}
