namespace Nms.Api.Domain;

public class DeviceSample
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset SampledAt { get; set; }
    public bool Reachable { get; set; }
    public double? LatencyMs { get; set; }
    public double? PacketLossPct { get; set; }
    public double? CpuPct { get; set; }
    public double? MemoryPct { get; set; }
    public double? InMbps { get; set; }
    public double? OutMbps { get; set; }
}

public class InterfaceSample
{
    public Guid InterfaceId { get; set; }
    public DateTimeOffset SampledAt { get; set; }
    public double InMbps { get; set; }
    public double OutMbps { get; set; }
    public long InErrors { get; set; }
    public long OutErrors { get; set; }
    public double UtilizationPct { get; set; }
}

public class DailyAvailability
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset Day { get; set; }
    public long Samples { get; set; }
    public long ReachableSamples { get; set; }
    public decimal? AvailabilityPct { get; set; }
    public double? AvgLatencyMs { get; set; }
    public double? PeakLatencyMs { get; set; }
}

public class HourlyInterfaceUtilization
{
    public Guid InterfaceId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset Hour { get; set; }
    public double AvgUtilizationPct { get; set; }
    public double PeakUtilizationPct { get; set; }
    public double AvgInMbps { get; set; }
    public double AvgOutMbps { get; set; }
    public long Errors { get; set; }
}
