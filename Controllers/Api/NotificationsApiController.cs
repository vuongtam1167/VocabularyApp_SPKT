using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers.Api;

[Route("api/notifications")]
public sealed class NotificationsApiController : ApiControllerBase
{
    private readonly NotificationService _notificationService;

    public NotificationsApiController(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationService.GetNotificationsAsync(CurrentUserId, take, cancellationToken);
        return Ok(notifications);
    }

    [HttpPost("{notificationId:long}/read")]
    public async Task<IActionResult> MarkAsRead(long notificationId, CancellationToken cancellationToken)
    {
        var updated = await _notificationService.MarkAsReadAsync(CurrentUserId, notificationId, cancellationToken);
        return updated ? Ok(new { message = "Đã đánh dấu đã đọc." }) : NotFound();
    }
}
