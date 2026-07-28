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
    public async Task<IActionResult> GetAll([FromQuery] bool? unreadOnly)
    {
        var result = await notificationQueryService.GetForUserAsync(currentUser.UserId, unreadOnly);
        return Ok(ApiResponse<IReadOnlyList<NotificationDto>>.Ok(result));
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        await notificationQueryService.MarkAsReadAsync(id, currentUser.UserId);
        return Ok(ApiResponse.Ok("Notification marked as read."));
    }
}
