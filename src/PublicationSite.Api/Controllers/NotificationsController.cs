using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
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
    /// This person's notifications, newest first, optionally only the unread ones. Every
    /// notification is delivered here whether or not email is switched on, so this is the
    /// record rather than a copy of one.
    /// </summary>
    /// <response code="200">The matching notifications, all of them.</response>
    /// <response code="401">No access token was sent, or the one sent has expired.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<NotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll([FromQuery] bool? unreadOnly)
    {
        var result = await notificationQueryService.GetForUserAsync(currentUser.UserId, unreadOnly);
        return Ok(ApiResponse<IReadOnlyList<NotificationDto>>.Ok(result));
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
