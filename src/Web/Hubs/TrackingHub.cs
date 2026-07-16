using Microsoft.AspNetCore.SignalR;

namespace SaigonWaterbus.Web.Hubs;

/// <summary>
/// Hub public cho realtime vị trí tàu. Thiết bị GPS vẫn gửi dữ liệu qua REST API;
/// hub chỉ broadcast vị trí đã được backend validate và lưu.
/// </summary>
public sealed class TrackingHub : Hub
{
    public const string BoatLocationEventName = "boatLocation";

    public static string BoatGroupName(Guid boatId) => $"boat:{boatId:N}";

    public Task JoinBoat(Guid boatId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, BoatGroupName(boatId));

    public Task LeaveBoat(Guid boatId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BoatGroupName(boatId));
}
