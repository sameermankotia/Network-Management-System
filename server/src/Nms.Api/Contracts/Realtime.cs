using Nms.Api.Domain;

namespace Nms.Api.Contracts;

public record DeviceStatusChanged(Guid DeviceId, string Name, DeviceStatus Status, DeviceStatus Previous, DateTimeOffset At);

public record SampleRecorded(
    Guid DeviceId,
    DateTimeOffset SampledAt,
    bool Reachable,
    double? LatencyMs,
    double? PacketLossPct,
    double? CpuPct,
    double? MemoryPct,
    double? InMbps,
    double? OutMbps);

public record LinkStatusChanged(Guid LinkId, Guid Source, Guid Target, LinkStatus Status, double? UtilizationPct);

public record CycleCompleted(
    DateTimeOffset At,
    int DevicesPolled,
    int Up,
    int Degraded,
    int Down,
    int ActiveAlerts,
    double TotalInMbps,
    double TotalOutMbps,
    long DurationMs);

/// The client surface of the monitoring hub. Keeping it typed means a renamed
/// event breaks the build instead of silently going nowhere.
public interface IMonitoringClient
{
    Task DeviceStatusChanged(DeviceStatusChanged payload);

    Task SampleRecorded(SampleRecorded payload);

    Task LinkStatusChanged(LinkStatusChanged payload);

    Task AlertRaised(AlertSummary alert);

    Task AlertChanged(AlertSummary alert);

    Task CycleCompleted(CycleCompleted payload);
}
