using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace SaigonWaterbus.Web.Hubs;

[Authorize]
public sealed class IncidentsHub : Hub
{
    public const string IncidentUpdatedEventName = "IncidentUpdated";
    public const string RescueDispatchedEventName = "RescueDispatched";
}
