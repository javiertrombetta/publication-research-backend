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

    [HttpPut("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        await notificationQueryService.MarkAsReadAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Notification marked as read."));
    }

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
