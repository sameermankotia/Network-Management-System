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
