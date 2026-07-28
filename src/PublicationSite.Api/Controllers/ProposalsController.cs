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
    [HttpPost("api/containers/{containerId:guid}/proposals")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Create(Guid containerId, [FromBody] SaveProposalRequest request)
    {
        var result = await proposalService.CreateAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse<ProposalDto>.Ok(result));
    }

    [HttpPut("api/proposals/{proposalId:guid}")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> Update(Guid proposalId, [FromBody] SaveProposalRequest request)
    {
        var result = await proposalService.UpdateAsync(proposalId, currentUser.UserId, request);
        return Ok(ApiResponse<ProposalDto>.Ok(result));
    }

    [HttpGet("api/containers/{containerId:guid}/proposals")]
    public async Task<IActionResult> GetByContainer(Guid containerId)
    {
        var result = await proposalService.GetByContainerAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }

    [HttpPost("api/containers/{containerId:guid}/proposals/finish-submission")]
    [Authorize(Roles = RoleNames.Student)]
    public async Task<IActionResult> FinishSubmission(Guid containerId)
    {
        await proposalService.FinishSubmissionAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals submitted."));
    }

    [HttpPost("api/containers/{containerId:guid}/proposals/request-resubmission")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    public async Task<IActionResult> RequestNewSubmission(Guid containerId, [FromBody] CommentsRequest request)
    {
        await proposalService.RequestNewSubmissionAsync(containerId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Student asked to resubmit proposals."));
    }

    [HttpGet("api/proposals/pending")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> GetPendingForCoordinator()
    {
        var result = await proposalService.GetPendingForCoordinatorAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }

    [HttpPost("api/proposals/send-to-supervisors")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> SendToSupervisors([FromBody] SendToSupervisorsRequest request)
    {
        await proposalService.SendToSupervisorsAsync(request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals sent to supervisors."));
    }

    [HttpGet("api/proposals/invited")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> GetInvited()
    {
        var result = await proposalService.GetInvitedProposalsForSupervisorAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<ProposalDto>>.Ok(result));
    }

    [HttpPost("api/proposals/{proposalId:guid}/supervisor-selection")]
    [Authorize(Roles = RoleNames.Supervisor)]
    public async Task<IActionResult> SelectAsFeasible(Guid proposalId, [FromBody] SupervisorSelectionRequest request)
    {
        await proposalService.SelectAsFeasibleAsync(proposalId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Proposal marked as feasible."));
    }

    [HttpGet("api/proposals/{proposalId:guid}/selections")]
    public async Task<IActionResult> GetSelections(Guid proposalId)
    {
        var result = await proposalService.GetSelectionsAsync(proposalId, currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<SupervisorInvitationDto>>.Ok(result));
    }

    [HttpPost("api/proposals/{proposalId:guid}/assign-supervisor")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> AssignSupervisor(Guid proposalId, [FromBody] AssignSupervisorRequest request)
    {
        await proposalService.AssignSupervisorAsync(proposalId, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Supervisor assigned."));
    }

    [HttpPost("api/containers/{containerId:guid}/proposals/defer-to-next-cycle")]
    [Authorize(Roles = RoleNames.Coordinator)]
    public async Task<IActionResult> DeferToNextCycle(Guid containerId, [FromBody] CommentsRequest request)
    {
        await proposalService.DeferToNextCycleAsync(containerId, request.Comments, currentUser.UserId);
        return Ok(ApiResponse.Ok("Proposals deferred to next cycle."));
    }
}
