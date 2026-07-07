using Microsoft.AspNetCore.SignalR;

namespace SaigonWaterbus.Web.Hubs;

/// <summary>
/// Hub realtime cho sơ đồ ghế: client join group theo tripId để nhận sự kiện
/// "SeatStatusChanged" khi có ghế được giữ / nhả / đặt thành công.
/// Xem sơ đồ ghế là public nên hub cho phép anonymous; mọi thao tác giữ ghế
/// vẫn đi qua REST API có xác thực.
/// </summary>
public sealed class TripSeatsHub : Hub
{
    public static string GroupName(Guid tripId) => $"trip-seats:{tripId:N}";

    public Task JoinTrip(Guid tripId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tripId));

    public Task LeaveTrip(Guid tripId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tripId));
}
