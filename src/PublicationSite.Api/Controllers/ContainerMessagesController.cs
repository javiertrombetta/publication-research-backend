using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Messages;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// People writing to each other about one publication, through the site rather than around it.
///
/// A student asks the person supervising them a question and the question stays with the research.
/// The alternative is personal email, where nothing said can be found again, least of all by the
/// supervisor who picks the student up next year.
///
/// Access to the publication is what gets somebody to this endpoint. It is not what lets them read
/// a conversation: every listing here is the caller's own correspondence, and a coordinator does
/// not thereby read what the student wrote to their supervisor.
/// </summary>
[ApiController]
[Authorize]
public class ContainerMessagesController(
    IContainerMessageService messageService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Whether this is switched on, who the caller may write to on this publication, and what a
    /// message may carry. What a screen needs before anybody has written anything.
    /// </summary>
    /// <response code="200">The state of messaging on this publication, for this person.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this publication is not yours to see.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpGet("api/containers/{containerId:guid}/messages/context")]
    [ProducesResponseType(typeof(ApiResponse<ContainerMessagingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContext(Guid containerId)
    {
        var result = await messageService.GetMessagingAsync(containerId, currentUser.UserId);
        return Ok(ApiResponse<ContainerMessagingDto>.Ok(result));
    }

    /// <summary>
    /// The caller's own correspondence on this publication, newest first.
    /// </summary>
    /// <param name="with">Narrows it to the exchange with one person. Left out, it returns the lot.</param>
    /// <response code="200">One page of it, with the total so the caller can draw a pager.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this publication is not yours to see.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpGet("api/containers/{containerId:guid}/messages")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ContainerMessageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessages(Guid containerId, [FromQuery] Guid? with, [FromQuery] PageRequest paging)
    {
        var result = await messageService.GetMessagesAsync(containerId, currentUser.UserId, with, paging);
        return Ok(ApiResponse<PagedResult<ContainerMessageDto>>.Ok(result));
    }

    /// <summary>
    /// Writes to somebody about this publication, with up to five files.
    ///
    /// The files are for what a message needs: a screenshot of the error, a photograph of a signed
    /// page. The documents a process asks for are uploaded where that process asks for them; one
    /// attached here is one nobody reviewing that process will ever see.
    /// </summary>
    /// <response code="200">The message, as sent.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but that is not somebody you can write to about this publication.</response>
    /// <response code="404">No publication container with that id.</response>
    /// <response code="422">Understood, and refused: messaging is switched off, the message is empty or too long, or it carries too many files.</response>
    [HttpPost("api/containers/{containerId:guid}/messages")]
    [RequestSizeLimit(100_000_000)]
    [ProducesResponseType(typeof(ApiResponse<ContainerMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Send(Guid containerId, [FromForm] SendContainerMessageForm form)
    {
        // Opened here and disposed here, after the service has copied each one. Opening them inside
        // the service would mean handing it IFormFile, and the service would then be tied to the
        // web layer for no gain.
        var streams = form.Files?.Select(f => f.OpenReadStream()).ToList() ?? [];

        try
        {
            var attachments = streams
                .Select((stream, index) => (Content: stream, FileName: form.Files![index].FileName))
                .ToList();

            var result = await messageService.SendAsync(
                containerId,
                currentUser.UserId,
                new SendContainerMessageRequest(form.RecipientUserId, form.Body),
                attachments);

            return Ok(ApiResponse<ContainerMessageDto>.Ok(result, "Sent."));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Marks as read what one person has sent the caller here, which is what opening a conversation
    /// does.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying how many; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this publication is not yours to see.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpPut("api/containers/{containerId:guid}/messages/read")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid containerId, [FromQuery] Guid with)
    {
        var count = await messageService.MarkReadAsync(containerId, currentUser.UserId, with);
        return Ok(ApiResponse.Ok(count == 0
            ? "Nothing there was unread."
            : count == 1 ? "1 message marked as read." : $"{count} messages marked as read."));
    }

    /// <summary>
    /// Opens a file that came with a message the caller sent or received.
    /// </summary>
    /// <response code="200">The file.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this publication is not yours to see.</response>
    /// <response code="404">No such attachment on this publication, or it belongs to a conversation you are not in.</response>
    [HttpGet("api/containers/{containerId:guid}/messages/attachments/{attachmentId:guid}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(Guid containerId, Guid attachmentId)
    {
        var (content, fileName) = await messageService.OpenAttachmentAsync(
            containerId, currentUser.UserId, attachmentId);

        return File(content, "application/octet-stream", fileName);
    }

    // ---------- What an administrator has decided about this publication ----------

    /// <summary>
    /// The rules in force on this publication, the people they could be about, and the roles they
    /// could be about.
    ///
    /// Administrators only. These rules decide who can say anything to whom, which is not a control
    /// to hand to the people it governs.
    /// </summary>
    /// <response code="200">The rules, the participants and the selectable roles.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication container with that id.</response>
    [HttpGet("api/containers/{containerId:guid}/messages/rules")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ContainerMessagingRulesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRules(Guid containerId)
    {
        var result = await messageService.GetRulesAsync(containerId);
        return Ok(ApiResponse<ContainerMessagingRulesDto>.Ok(result));
    }

    /// <summary>
    /// Stops, or allows, messages on this publication for the whole publication, for a role on it,
    /// or for one named person. Replaces whatever was already said about the same target.
    ///
    /// A rule works both ways: somebody it stops neither writes nor is written to here. It also
    /// closes a conversation they were already in, which is the difference between this and
    /// narrowing the institution's settings.
    /// </summary>
    /// <response code="200">The rule, as it now stands.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No publication container with that id, or no such person.</response>
    /// <response code="422">Understood, and refused: both a role and a person were named, the role is not one here, or no reason was given.</response>
    [HttpPut("api/containers/{containerId:guid}/messages/rules")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ContainerMessagingRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetRule(Guid containerId, [FromBody] SetContainerMessagingRuleRequest request)
    {
        var result = await messageService.SetRuleAsync(containerId, currentUser.UserId, request);
        return Ok(ApiResponse<ContainerMessagingRuleDto>.Ok(result, "Saved."));
    }

    /// <summary>
    /// Takes a rule away, so this publication follows the institution's settings again for whoever
    /// it was about.
    /// </summary>
    /// <response code="200">Gone. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this is not something your role may do.</response>
    /// <response code="404">No such rule on this publication.</response>
    [HttpDelete("api/containers/{containerId:guid}/messages/rules/{ruleId:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRule(Guid containerId, Guid ruleId)
    {
        await messageService.RemoveRuleAsync(containerId, currentUser.UserId, ruleId);
        return Ok(ApiResponse.Ok("Removed. This publication follows the institution's settings again."));
    }
}
