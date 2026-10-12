using Nms.Api.Domain;

namespace Nms.Api.Contracts;

public static class Map
{
    public static AlertSummary Alert(Alert alert, string deviceName, string? interfaceName = null, string? ruleName = null) => new(
        alert.Id,
        alert.DeviceId,
        deviceName,
        alert.InterfaceId,
        interfaceName,
        alert.RuleId,
        ruleName ?? alert.Rule?.Name,
        alert.Severity,
        alert.State,
        alert.Title,
        alert.Detail,
        alert.Metric,
        alert.ObservedValue,
        alert.Threshold,
        alert.RaisedAt,
        alert.LastSeenAt,
        alert.Occurrences,
        alert.AcknowledgedAt,
        alert.AcknowledgedBy,
        alert.ResolvedAt);

    public static DeviceSummary Device(Device device, DeviceSample? latest, int openAlerts) => new(
        device.Id,
        device.Name,
        device.Hostname,
        device.IpAddress.ToString(),
        device.DeviceType,
        device.Vendor,
        device.Model,
        device.Site,
        device.Building,
        device.Room,
        device.Status,
        device.IsMonitored,
        device.PollIntervalSeconds,
        device.LastSeenAt,
        device.StatusChangedAt,
        device.UptimeSeconds,
        device.Tags,
        latest?.LatencyMs,
        latest?.PacketLossPct,
        latest?.CpuPct,
        latest?.MemoryPct,
        latest?.InMbps,
        latest?.OutMbps,
        openAlerts);

    public static DiagnosticSummary Diagnostic(DiagnosticRun run) => new(
        run.Id,
        run.DeviceId,
        run.Kind,
        run.State,
        run.Success,
        run.Summary,
        run.RequestedBy,
        run.StartedAt,
        run.CompletedAt,
        run.Output);

    public static AlertEventEntry Event(AlertEvent entry) =>
        new(entry.Id, entry.Kind, entry.Message, entry.Actor, entry.CreatedAt);
}
