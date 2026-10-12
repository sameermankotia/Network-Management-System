namespace Nms.Api.Monitoring;

public record ProbeResult(bool Reachable, double? LatencyMs, double PacketLossPct, string Method)
{
    public static ProbeResult Unreachable(string method) => new(false, null, 100, method);
}

public record ResourceReading(double CpuPct, double MemoryPct, double InMbps, double OutMbps);

public record InterfaceReading(Guid InterfaceId, double InMbps, double OutMbps, long InErrors, long OutErrors, double UtilizationPct);
