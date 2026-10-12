using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nms.Api.Contracts;

namespace Nms.Api.Hubs;

[Authorize]
public class MonitoringHub : Hub<IMonitoringClient>
{
    public const string DeviceGroupPrefix = "device:";

    public Task WatchDevice(Guid deviceId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroupPrefix + deviceId);

    public Task UnwatchDevice(Guid deviceId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, DeviceGroupPrefix + deviceId);
}
