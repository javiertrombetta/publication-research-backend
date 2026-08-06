using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Support;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Writing to the institution's IT desk.
///
/// Signed in only, and deliberately. The desk is offered to visitors on the sign-in page when the
/// institution has said so, but as an address to write to from their own mail client. A form open
/// to the world that sends email with attachments to a fixed address is a relay for anybody who
/// finds it, and the people this desk supports are the institution's own students and staff, who
/// have accounts.
/// </summary>
[ApiController]
[Route("api/support")]
[Authorize]
public class SupportController(ISupportService supportService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Whether the desk can be written to from inside the site, and its address for when it
    /// cannot. A screen asks this before deciding whether to offer a form or a mail link.
    /// </summary>
    /// <response code="200">The options.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("contact")]
    [ProducesResponseType(typeof(ApiResponse<SupportContactOptionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetContactOptions()
    {
        var result = await supportService.GetContactOptionsAsync();
        return Ok(ApiResponse<SupportContactOptionsDto>.Ok(result));
    }

    /// <summary>
    /// Sends a message to the IT desk, with up to three files.
    ///
    /// Nothing is stored: the desk is a mailbox rather than a role here, so there is no
    /// notification to raise and no record to keep beyond the audit line saying it was sent. The
    /// reply-to is the sender's own address, so the answer reaches them rather than the site.
    /// </summary>
    /// <response code="200">Sent.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="422">Understood, and refused: no address is configured, no mail server is configured, the message is empty or too long, or a file is too large.</response>
    [HttpPost("contact")]
    [RequestSizeLimit(40_000_000)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Contact([FromForm] ContactSupportForm form)
    {
        var files = form.Files ?? [];
        var streams = files.Select(f => f.OpenReadStream()).ToList();

        try
        {
            var attachments = streams
                .Select((stream, index) => (Content: stream, files[index].FileName, files[index].Length))
                .ToList();

            await supportService.SendToItSupportAsync(
                currentUser.UserId, form.Subject, form.Body, attachments);

            return Ok(ApiResponse.Ok("Sent to the IT desk. They will reply to your email address."));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }
}
