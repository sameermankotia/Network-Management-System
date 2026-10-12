using Microsoft.AspNetCore.SignalR;
using Nms.Api.Contracts;

namespace Nms.Api.Hubs;

/// Fan-out for live updates. Per-device samples only reach clients that asked for
/// that device; status changes, alerts and cycle summaries go to everyone.
public class MonitoringBroadcaster(IHubContext<MonitoringHub, IMonitoringClient> hub)
{
    public Task StatusChanged(DeviceStatusChanged payload) =>
        hub.Clients.All.DeviceStatusChanged(payload);

    public Task SampleRecorded(SampleRecorded payload) =>
        hub.Clients.Group(MonitoringHub.DeviceGroupPrefix + payload.DeviceId).SampleRecorded(payload);

    public Task LinkChanged(LinkStatusChanged payload) =>
        hub.Clients.All.LinkStatusChanged(payload);

    public Task AlertRaised(AlertSummary alert) =>
        hub.Clients.All.AlertRaised(alert);

    public Task AlertChanged(AlertSummary alert) =>
        hub.Clients.All.AlertChanged(alert);

    public Task CycleCompleted(CycleCompleted payload) =>
        hub.Clients.All.CycleCompleted(payload);
}
