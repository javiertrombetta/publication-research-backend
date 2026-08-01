using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Proposals;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Authorize]
public class ProposalsController(IProposalService proposalService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Adds a proposal to the student's publication. Up to three may be drafted, and they stay
    /// editable until the student says they have finished.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/proposals")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<ProposalDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid containerId, [FromBody] SaveProposalRequest request)
    {
        var result = await proposalService.CreateAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse<ProposalDto>.Ok(result));
    }
    /// <summary>
    /// Edits one of the student's own draft proposals, while they are still drafts.
    /// </summary>
    [HttpPut("api/proposals/{proposalId:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<ProposalDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid proposalId, [FromBody] SaveProposalRequest request)
    {
        var result = await proposalService.UpdateAsync(proposalId, currentUser.UserId, request);
        return Ok(ApiResponse<ProposalDto>.Ok(result));
    }
    /// <summary>
    /// The proposals on a publication, with where each one has got to.
    /// </summary>
    [HttpGet("api/containers/{containerId:guid}/proposals")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProposalDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByContainer(Guid containerId)
    {
        var result = await proposalService.GetByContainerAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }
    /// <summary>
    /// The student declares their proposals final. They stop being editable and reach the
    /// coordinator, who decides which supervisors to consult.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/proposals/finish-submission")]
    [Authorize(Roles = RoleNames.Student)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FinishSubmission(Guid containerId)
    {
        await proposalService.FinishSubmissionAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals submitted."));
    }
    /// <summary>
    /// Reopens the proposals for editing after they were sent back, so the student can answer
    /// what was actually objected to rather than start again.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/proposals/request-resubmission")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestNewSubmission(Guid containerId, [FromBody] CommentsRequest request)
    {
        await proposalService.RequestNewSubmissionAsync(containerId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Student asked to resubmit proposals."));
    }
    /// <summary>
    /// The publications with proposals in this coordinator's hands, one page at a time — either
    /// everything they hold, or only those still needing a supervisor allocated.
    /// </summary>
    [HttpGet("api/proposals/for-coordinator")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProposalWithInvitationsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForCoordinator([FromQuery] PageRequest paging, [FromQuery] bool awaitingAllocation = false)
    {
        var result = await proposalService.GetForCoordinatorAsync(currentUser.UserId, paging, awaitingAllocation);
        return Ok(ApiResponse<PagedResult<ProposalWithInvitationsDto>>.Ok(result));
    }


    /// <summary>
    /// The proposals from students in the department this person heads, abstracts included, so
    /// the head can read what is being proposed without opening each one.
    /// </summary>
    [HttpGet("api/proposals/in-my-department")]
    [Authorize(Roles = RoleNames.HeadOfDepartment)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProposalWithInvitationsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInMyDepartment([FromQuery] PageRequest paging)
    {
        var result = await proposalService.GetInDepartmentAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<ProposalWithInvitationsDto>>.Ok(result));
    }


    /// <summary>
    /// The proposals waiting on the coordinator right now, when the whole listing is more than
    /// is wanted.
    /// </summary>
    [HttpGet("api/proposals/pending")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProposalDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingForCoordinator([FromQuery] PageRequest paging)
    {
        var result = await proposalService.GetPendingForCoordinatorAsync(currentUser.UserId, paging);
        return Ok(ApiResponse<PagedResult<ProposalDto>>.Ok(result));
    }
    /// <summary>
    /// Asks a chosen set of supervisors whether they could supervise these proposals. Sending
    /// the same person twice is treated as sending once.
    /// </summary>
    [HttpPost("api/proposals/send-to-supervisors")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendToSupervisors([FromBody] SendToSupervisorsRequest request)
    {
        await proposalService.SendToSupervisorsAsync(request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals sent to supervisors."));
    }
    /// <summary>
    /// The proposals this supervisor has been asked about, with what they need to judge
    /// feasibility.
    /// </summary>
    [HttpGet("api/proposals/invited")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProposalDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvited()
    {
        var result = await proposalService.GetInvitedProposalsForSupervisorAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }
    /// <summary>
    /// The supervisor's answer: they could supervise this, or they could not, with their
    /// reasoning. It is an expression of interest — the coordinator still chooses who gets it.
    /// </summary>
    [HttpPost("api/proposals/{proposalId:guid}/supervisor-selection")]
    [Authorize(Roles = RoleNames.Supervisor)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectAsFeasible(Guid proposalId, [FromBody] SupervisorSelectionRequest request)
    {
        await proposalService.SelectAsFeasibleAsync(proposalId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Proposal marked as feasible."));
    }
    /// <summary>
    /// Which supervisors answered on a proposal and what they said, which is what the
    /// coordinator allocates from.
    /// </summary>
    [HttpGet("api/proposals/{proposalId:guid}/selections")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SupervisorInvitationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSelections(Guid proposalId)
    {
        var result = await proposalService.GetSelectionsAsync(proposalId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<SupervisorInvitationDto>>.Ok(result));
    }
    /// <summary>
    /// The coordinator allocates the proposal to one supervisor. That settles which proposal
    /// the publication proceeds with, and the student moves on to ethics.
    /// </summary>
    [HttpPost("api/proposals/{proposalId:guid}/assign-supervisor")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignSupervisor(Guid proposalId, [FromBody] AssignSupervisorRequest request)
    {
        await proposalService.AssignSupervisorAsync(proposalId, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Supervisor assigned."));
    }
    /// <summary>
    /// Holds a publication over to the next cycle when no supervisor could take it — better
    /// than refusing work nobody was available for.
    /// </summary>
    [HttpPost("api/containers/{containerId:guid}/proposals/defer-to-next-cycle")]
    [Authorize(Roles = RoleNames.Coordinator)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeferToNextCycle(Guid containerId, [FromBody] CommentsRequest request)
    {
        await proposalService.DeferToNextCycleAsync(containerId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals deferred to next cycle."));
    }
}
