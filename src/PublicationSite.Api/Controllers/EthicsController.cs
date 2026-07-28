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
    [HttpGet("api/ethics/guidance")]
    [AllowAnonymous]
    public IActionResult GetGuidance() => Ok(ApiResponse<EthicsGuidanceDto>.Ok(ethicsService.GetGuidance()));

    [HttpPost("api/containers/{containerId:guid}/ethics/declaration")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> SubmitDeclaration(Guid containerId, [FromBody] EthicsDeclarationRequest request)
    {
        var result = await ethicsService.SubmitDeclarationAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse<EthicsDeclarationDto>.Ok(result));
    }

    [HttpGet("api/containers/{containerId:guid}/ethics")]
    public async Task<IActionResult> GetApproval(Guid containerId)
    {
        var result = await ethicsService.GetApprovalAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<EthicsApprovalDto>.Ok(result));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/supervisor-decision")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> SupervisorDecision(Guid containerId, [FromBody] SupervisorRequirementDecisionRequest request)
    {
        await ethicsService.SubmitSupervisorRequirementDecisionAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/documents")]
    [Authorize(Roles = RoleNames.Student)]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadDocument(Guid containerId, [FromForm] EthicsDocumentUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        var result = await ethicsService.UploadDocumentAsync(containerId, currentUser.UserId, form.DocumentType, stream, form.File.FileName);
        return Ok(ApiResponse<EthicsDocumentDto>.Ok(result));
    }

    [HttpGet("api/containers/{containerId:guid}/ethics/documents")]
    public async Task<IActionResult> GetDocuments(Guid containerId)
    {
        var result = await ethicsService.GetDocumentsAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<EthicsDocumentDto>>.Ok(result));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/supervisor-review")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> SupervisorReview(Guid containerId, [FromBody] DocumentReviewDecisionRequest request)
    {
        await ethicsService.SupervisorReviewDocumentsAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-not-required-review")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> CoordinatorNotRequiredReview(Guid containerId, [FromBody] CoordinatorNotRequiredReviewRequest request)
    {
        await ethicsService.CoordinatorReviewNotRequiredAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-document-review")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> CoordinatorDocumentReview(Guid containerId, [FromBody] CoordinatorDocumentReviewRequest request)
    {
        await ethicsService.CoordinatorReviewDocumentsAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Review recorded."));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/hod-review")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    public async Task<IActionResult> HeadOfDepartmentReview(Guid containerId, [FromBody] HeadOfDepartmentReviewRequest request)
    {
        await ethicsService.HeadOfDepartmentReviewAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Comments recorded."));
    }

    [HttpPost("api/containers/{containerId:guid}/ethics/coordinator-final-decision")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> CoordinatorFinalDecision(Guid containerId, [FromBody] CoordinatorFinalDecisionRequest request)
    {
        await ethicsService.CoordinatorFinalDecisionAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Final decision recorded."));
    }
}
