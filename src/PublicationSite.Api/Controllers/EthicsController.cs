using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Ethics;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Authorize]
public class EthicsController(IEthicsService ethicsService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// The questions a student should ask themselves before declaring whether their research
    /// needs ethics approval, and what each answer means. Open to anyone, including someone not
    /// yet signed in — it is guidance, not a record.
    /// </summary>
    [HttpGet("api/ethics/guidance")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<EthicsGuidanceDto>), StatusCodes.Status200OK)]
    public IActionResult GetGuidance() => Ok(ApiResponse<EthicsGuidanceDto>.Ok(ethicsService.GetGuidance()));
    /// <summary>
    /// The student's own declaration: does this research need ethics approval? An answer of no,
    /// or of unsure, does not settle it. The supervisor decides either way, and the coordinator
    /// confirms that decision, so nobody rules themselves out of ethics review.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/ethics/declaration")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<EthicsDeclarationDto>), StatusCodes.Status200OK)]
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
    [HttpGet("api/containers/{containerId:guid}/ethics")]
    [ProducesResponseType(typeof(ApiResponse<EthicsApprovalDto>), StatusCodes.Status200OK)]
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
    [HttpPost("api/containers/{containerId:guid}/ethics/supervisor-decision")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
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
    [HttpPost("api/containers/{containerId:guid}/ethics/documents")]
    [Authorize(Roles = RoleNames.Student)]
    [RequestSizeLimit(100_000_000)]
    [ProducesResponseType(typeof(ApiResponse<EthicsDocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadDocument(Guid containerId, [FromForm] EthicsDocumentUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await ethicsService.UploadDocumentAsync(containerId, currentUser.UserId, form.DocumentType, stream, form.File.FileName);
        return Ok(ApiResponse<EthicsDocumentDto>.Ok(result));
    }

    /// <summary>
    /// What this publication has been asked to supply, and what is still outstanding.
    /// </summary>
    [HttpGet("api/containers/{containerId:guid}/ethics/required-documents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RequiredEthicsDocumentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequiredDocuments(Guid containerId)
    {
        var result = await ethicsService.GetRequiredDocumentsAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<RequiredEthicsDocumentDto>>.Ok(result));
    }

    /// <summary>
    /// The ethics documents uploaded so far, with each one's review state and the reviewer's
    /// comments.
    /// </summary>
    [HttpGet("api/containers/{containerId:guid}/ethics/documents")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EthicsDocumentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocuments(Guid containerId)
    {
        var result = await ethicsService.GetDocumentsAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<EthicsDocumentDto>>.Ok(result));
    }

    /// <summary>
    /// One uploaded document, so the people asked to approve it can read it first.
    /// </summary>
    [HttpGet("api/containers/{containerId:guid}/ethics/documents/{documentId:guid}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
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
    [HttpPost("api/containers/{containerId:guid}/ethics/supervisor-review")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
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
    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-not-required-review")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CoordinatorNotRequiredReview(Guid containerId, [FromBody] CoordinatorNotRequiredReviewRequest request)
    {
        await ethicsService.CoordinatorReviewNotRequiredAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    /// <summary>
    /// The coordinator's review of documents the supervisor has already accepted, before they
    /// go to the head of department.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-document-review")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CoordinatorDocumentReview(Guid containerId, [FromBody] CoordinatorDocumentReviewRequest request)
    {
        await ethicsService.CoordinatorReviewDocumentsAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    /// <summary>
    /// The head of department's review — the last academic check before the coordinator closes
    /// the ethics stage.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/ethics/hod-review")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> HeadOfDepartmentReview(Guid containerId, [FromBody] HeadOfDepartmentReviewRequest request)
    {
        await ethicsService.HeadOfDepartmentReviewAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Comments recorded."));
    }

    /// <summary>
    /// Closes the ethics stage. Approval unblocks the paper; refusal stops the publication
    /// there, with the reason recorded against it.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-final-decision")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CoordinatorFinalDecision(Guid containerId, [FromBody] CoordinatorFinalDecisionRequest request)
    {
        await ethicsService.CoordinatorFinalDecisionAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Final decision recorded."));
    }
}
