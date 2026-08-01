using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Research proposals, from the student's drafts to the supervisor who ends up taking one.
///
/// A student writes up to three and declares them final; the coordinator asks a set of supervisors
/// whether they could supervise them; those who could say so, with their reasoning; and the
/// coordinator allocates one. That allocation is what settles which proposal the publication
/// proceeds with. Where nobody was available, it is held over to the next cycle rather than refused.
/// </summary>
[ApiController]
[Authorize]
public class ProposalsController(IProposalService proposalService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Adds a proposal to the student's publication. Up to three may be drafted, and they stay
    /// editable until the student says they have finished.
    /// </summary>
    /// <response code="200">The proposal.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/proposals")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<ProposalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(Guid containerId, [FromBody] SaveProposalRequest request)
    {
        var result = await proposalService.CreateAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse<ProposalDto>.Ok(result));
    }
    /// <summary>
    /// Edits one of the student's own draft proposals, while they are still drafts.
    /// </summary>
    /// <response code="200">The proposal.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No research proposal with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPut("api/proposals/{proposalId:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<ProposalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid proposalId, [FromBody] SaveProposalRequest request)
    {
        var result = await proposalService.UpdateAsync(proposalId, currentUser.UserId, request);
        return Ok(ApiResponse<ProposalDto>.Ok(result));
    }
    /// <summary>
    /// The proposals on a publication, with where each one has got to.
    /// </summary>
    /// <response code="200">The matching proposals, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    [HttpGet("api/containers/{containerId:guid}/proposals")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProposalDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByContainer(Guid containerId)
    {
        var result = await proposalService.GetByContainerAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }
    /// <summary>
    /// The student declares their proposals final. They stop being editable and reach the
    /// coordinator, who decides which supervisors to consult.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/containers/{containerId:guid}/proposals/finish-submission")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> FinishSubmission(Guid containerId)
    {
        await proposalService.FinishSubmissionAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals submitted."));
    }
    /// <summary>
    /// Reopens the proposals for editing after they were sent back, so the student can answer
    /// what was actually objected to rather than start again.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpPost("api/containers/{containerId:guid}/proposals/request-resubmission")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestNewSubmission(Guid containerId, [FromBody] CommentsRequest request)
    {
        await proposalService.RequestNewSubmissionAsync(containerId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Student asked to resubmit proposals."));
    }
    /// <summary>
    /// The publications with proposals in this coordinator's hands, one page at a time: either
    /// everything they hold, or only those still needing a supervisor allocated.
    /// </summary>
    /// <response code="200">One page of proposals with the supervisors invited to them, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/proposals/for-coordinator")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProposalWithInvitationsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForCoordinator([FromQuery] PageRequest paging, [FromQuery] bool awaitingAllocation = false)
    {
        var result = await proposalService.GetForCoordinatorAsync(currentUser.UserId, paging, awaitingAllocation);
        return Ok(ApiResponse<PagedResult<ProposalWithInvitationsDto>>.Ok(result));
    }


    /// <summary>
    /// The proposals from students in the department this person heads, abstracts included, so
    /// the head can read what is being proposed without opening each one.
    /// </summary>
    /// <response code="200">One page of proposals with the supervisors invited to them, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/proposals/in-my-department")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProposalWithInvitationsDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInMyDepartment([FromQuery] PageRequest paging)
    {
        var result = await proposalService.GetInDepartmentAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<ProposalWithInvitationsDto>>.Ok(result));
    }


    /// <summary>
    /// The proposals waiting on the coordinator right now, when the whole listing is more than
    /// is wanted.
    /// </summary>
    /// <response code="200">One page of proposals, with the total count alongside it so a pager can be drawn without a second request.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/proposals/pending")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProposalDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingForCoordinator([FromQuery] PageRequest paging)
    {
        var result = await proposalService.GetPendingForCoordinatorAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<ProposalDto>>.Ok(result));
    }
    /// <summary>
    /// Asks a chosen set of supervisors whether they could supervise these proposals. Sending
    /// the same person twice is treated as sending once.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/proposals/send-to-supervisors")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SendToSupervisors([FromBody] SendToSupervisorsRequest request)
    {
        await proposalService.SendToSupervisorsAsync(request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals sent to supervisors."));
    }
    /// <summary>
    /// The proposals this supervisor has been asked about, with what they need to judge
    /// feasibility.
    /// </summary>
    /// <response code="200">The matching proposals, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    [HttpGet("api/proposals/invited")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProposalDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvited()
    {
        var result = await proposalService.GetInvitedProposalsForSupervisorAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }
    /// <summary>
    /// The supervisor's answer: they could supervise this, or they could not, with their reasoning.
    /// It is an expression of interest, and the coordinator still chooses who gets it.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    [HttpPost("api/proposals/{proposalId:guid}/supervisor-selection")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SelectAsFeasible(Guid proposalId, [FromBody] SupervisorSelectionRequest request)
    {
        await proposalService.SelectAsFeasibleAsync(proposalId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Proposal marked as feasible."));
    }
    /// <summary>
    /// Which supervisors answered on a proposal and what they said, which is what the
    /// coordinator allocates from.
    /// </summary>
    /// <response code="200">The matching invitations sent to supervisors, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No research proposal with that id.</response>
    [HttpGet("api/proposals/{proposalId:guid}/selections")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SupervisorInvitationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSelections(Guid proposalId)
    {
        var result = await proposalService.GetSelectionsAsync(proposalId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<SupervisorInvitationDto>>.Ok(result));
    }
    /// <summary>
    /// The coordinator allocates the proposal to one supervisor. That settles which proposal
    /// the publication proceeds with, and the student moves on to ethics.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but not entitled to this: either the role is wrong for the endpoint, or the record belongs to somebody else.</response>
    /// <response code="404">No research proposal with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("api/proposals/{proposalId:guid}/assign-supervisor")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignSupervisor(Guid proposalId, [FromBody] AssignSupervisorRequest request)
    {
        await proposalService.AssignSupervisorAsync(proposalId, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Supervisor assigned."));
    }
    /// <summary>
    /// Holds a publication over to the next cycle when no supervisor could take it, which is better
    /// than refusing work nobody was available for.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpPost("api/containers/{containerId:guid}/proposals/defer-to-next-cycle")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeferToNextCycle(Guid containerId, [FromBody] CommentsRequest request)
    {
        await proposalService.DeferToNextCycleAsync(containerId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals deferred to next cycle."));
    }
}
