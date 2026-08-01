using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Notifications;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

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
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<NotificationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool? unreadOnly)
    {
        var result = await notificationQueryService.GetForUserAsync(currentUser.UserId, unreadOnly);
        return Ok(ApiResponse<IReadOnlyList<NotificationDto>>.Ok(result));
    }

    /// <summary>
    /// How many are unread. Requested on every page load to colour the top bar's bell, so it is
    /// deliberately a count rather than a listing the caller has to measure.
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var result = await notificationQueryService.GetUnreadCountAsync(currentUser.UserId);
        return Ok(ApiResponse<int>.Ok(result));
    }

    /// <summary>
    /// Marks one as read — what opening it does.
    /// </summary>
    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        await notificationQueryService.MarkAsReadAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Notification marked as read."));
    }

    /// <summary>
    /// Clears the whole unread count in one go, and says how many that was.
    /// </summary>
    [HttpPut("read")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var count = await notificationQueryService.MarkAllAsReadAsync(currentUser.UserId);
        return Ok(ApiResponse.Ok(count == 0
            ? "You had nothing unread."
            : count == 1 ? "1 notification marked as read." : $"{count} notifications marked as read."));
    }
}
