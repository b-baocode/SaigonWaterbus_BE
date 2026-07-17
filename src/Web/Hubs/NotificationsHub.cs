using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SaigonWaterbus.Web.Hubs;

/// <summary>
/// Kênh realtime cho in-app notification. Client kết nối với access_token; server đẩy
/// sự kiện tới đúng user qua Clients.User (SignalR map user từ claim NameIdentifier),
/// nên hub không cần join group thủ công.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string CreatedEventName = "NotificationCreated";
}
