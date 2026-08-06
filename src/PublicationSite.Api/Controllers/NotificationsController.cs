using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Notifications;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// What is waiting for this person, and what they have already seen. Every notification is raised in
/// the application whether or not email is configured, so turning email off loses nothing.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(INotificationQueryService notificationQueryService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// One page of this person's notifications, newest first, optionally only the unread ones and
    /// optionally matching a search. Every notification is delivered here whether or not email is
    /// switched on, so this is the record rather than a copy of one.
    /// </summary>
    /// <response code="200">The matching page, with the total so the caller can draw a pager.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <param name="unreadOnly">Only what this person has not read yet, which is what the bell in the top bar shows.</param>
    /// <param name="search">Matched against the title and the message of each notification. Ignored when blank.</param>
    /// <param name="paging">Which page, and how long. Left out, the institution's configured page size applies.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? unreadOnly, [FromQuery] string? search, [FromQuery] PageRequest paging)
    {
        var result = await notificationQueryService.GetForUserAsync(currentUser.UserId, unreadOnly, search, paging);
        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(result));
    }

    /// <summary>
    /// One notification of this person's, by id.
    ///
    /// For a caller that has an id and wants what it points at. Searching a page for it stopped
    /// working the moment this list was paged, and paging through the lot to find one is not a
    /// thing to ask of anybody.
    /// </summary>
    /// <response code="200">The notification.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="404">No notification with that id, or it belongs to somebody else.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOne(Guid id)
    {
        var result = await notificationQueryService.GetOneAsync(id, currentUser.UserId);

        // Somebody else's notification is reported as missing rather than as forbidden. Saying
        // "that one is not yours" confirms it exists, and there is nothing here worth confirming.
        return result is null
            ? NotFound(ApiResponse.Fail("No notification with that id."))
            : Ok(ApiResponse<NotificationDto>.Ok(result));
    }

    /// <summary>
    /// How many are unread. Requested on every page load to colour the top bar's bell, so it is
    /// deliberately a count rather than a listing the caller has to measure.
    /// </summary>
    /// <response code="200">The count, in the envelope.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var result = await notificationQueryService.GetUnreadCountAsync(currentUser.UserId);
        return Ok(ApiResponse<int>.Ok(result));
    }

    /// <summary>
    /// Marks one as read, which is what opening it does.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No notification with that id.</response>
    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        await notificationQueryService.MarkAsReadAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Notification marked as read."));
    }

    /// <summary>
    /// Clears the whole unread count in one go, and says how many that was.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpPut("read")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var count = await notificationQueryService.MarkAllAsReadAsync(currentUser.UserId);
        return Ok(ApiResponse.Ok(count == 0
            ? "You had nothing unread."
            : count == 1 ? "1 notification marked as read." : $"{count} notifications marked as read."));
    }
}
