using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Committees;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Authorize]
public class CommitteesController(ICommitteeService committeeService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("api/publications/{publicationId:guid}/assign-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Assign(Guid publicationId, [FromBody] AssignCommitteeRequest request)
    {
        var result = await committeeService.AssignAsync(publicationId, request, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result));
    }

    [HttpGet("api/publications/{publicationId:guid}/committee")]
    public async Task<IActionResult> GetByPublication(Guid publicationId)
    {
        var result = await committeeService.GetByPublicationAsync(publicationId, currentUser.UserId);
        return Ok(ApiResponse<CommitteeDto>.Ok(result));
    }

    [HttpGet("api/committees/my-assignments")]
    [Authorize(Roles = $"{RoleNames.InternalCommitteeMember},{RoleNames.ExternalCommitteeMember}")]
    public async Task<IActionResult> GetMyAssignments()
    {
        var result = await committeeService.GetAssignmentsForMemberAsync(currentUser.UserId);
        return Ok(ApiResponse<IReadOnlyList<CommitteeDto>>.Ok(result));
    }

    [HttpPost("api/committees/{committeeId:guid}/review")]
    [Authorize(Roles = $"{RoleNames.InternalCommitteeMember},{RoleNames.ExternalCommitteeMember}")]
    public async Task<IActionResult> MemberReview(Guid committeeId, [FromBody] CommitteeMemberReviewRequest request)
    {
        await committeeService.MemberReviewAsync(committeeId, currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Decision recorded."));
    }

    [HttpGet("api/settings/default-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> GetDefaultConfig()
    {
        var result = await committeeService.GetDefaultConfigAsync();
        return Ok(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>.Ok(result));
    }

    [HttpPut("api/settings/default-committee")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> SetDefaultConfig([FromBody] SetCommitteeRoleConfigRequest request)
    {
        await committeeService.SetDefaultConfigAsync(request);
        return Ok(ApiResponse.Ok("Default committee configuration updated."));
    }

    [HttpGet("api/committees/{committeeId:guid}/config")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    public async Task<IActionResult> GetCommitteeConfig(Guid committeeId)
    {
        var result = await committeeService.GetCommitteeConfigAsync(committeeId);
        return Ok(ApiResponse<IReadOnlyList<CommitteeRoleConfigDto>>.Ok(result));
    }

    [HttpPut("api/committees/{committeeId:guid}/config")]
    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Coordinator}")]
    public async Task<IActionResult> SetCommitteeConfig(Guid committeeId, [FromBody] SetCommitteeRoleConfigRequest request)
    {
        await committeeService.SetCommitteeConfigAsync(committeeId, request, currentUser.UserId);
        return Ok(ApiResponse.Ok("Committee configuration updated."));
    }
}
