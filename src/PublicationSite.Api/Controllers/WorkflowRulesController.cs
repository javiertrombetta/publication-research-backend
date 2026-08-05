using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Settings;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// The settings every role's screens have to read to draw themselves honestly: which decisions
/// ask for a reason, and which steps of the pipeline this institution runs.
///
/// Apart from SettingsController, which is an administrator's screen and is restricted to that
/// role as a whole. An action cannot loosen a role rule declared on its controller, only
/// abandon authorisation altogether, so the endpoints anybody signed in must be able to read
/// live here instead. They are readable, never writable: changing any of this stays with the
/// administrator.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize]
public class WorkflowRulesController(ISystemSettingService systemSettingService) : ControllerBase
{
    /// <summary>
    /// Every decision in the pipeline that carries a comment, and whether this institution asks
    /// for one on it.
    ///
    /// Read by the screens where these decisions are made, so that a button says which of them
    /// needs a reason before somebody presses one.
    /// </summary>
    /// <response code="200">Every decision, with what it is set to and what it would be by default.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("decision-comments")]
    [ProducesResponseType(typeof(ApiResponse<DecisionCommentSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetDecisionComments()
    {
        var result = await systemSettingService.GetDecisionCommentSettingsAsync();
        return Ok(ApiResponse<DecisionCommentSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Which of the research paper's three readings this institution runs. Read by the screens
    /// that offer them, so a step that is off is not offered to anybody.
    /// </summary>
    /// <response code="200">The paper workflow settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("paper-workflow")]
    [ProducesResponseType(typeof(ApiResponse<PaperWorkflowSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaperWorkflow()
    {
        var result = await systemSettingService.GetPaperWorkflowSettingsAsync();
        return Ok(ApiResponse<PaperWorkflowSettingsDto>.Ok(result));
    }

    /// <summary>
    /// Which optional steps of the ethics pipeline this institution runs.
    ///
    /// Read by every ethics screen, which has to say who decides next. A coordinator told the
    /// Head of Department has commented on a publication nobody has read is worse off than one
    /// told nothing at all.
    /// </summary>
    /// <response code="200">The ethics workflow settings.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("ethics-workflow")]
    [ProducesResponseType(typeof(ApiResponse<EthicsWorkflowSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEthicsWorkflow()
    {
        var result = await systemSettingService.GetEthicsWorkflowSettingsAsync();
        return Ok(ApiResponse<EthicsWorkflowSettingsDto>.Ok(result));
    }
}
