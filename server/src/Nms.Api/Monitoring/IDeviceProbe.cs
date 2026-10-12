using Nms.Api.Domain;

namespace Nms.Api.Monitoring;

public interface IDeviceProbe
{
    Task<ProbeResult> ProbeAsync(Device device, CancellationToken ct);
}

/// CPU, memory and interface counters. A production deployment swaps this for an
/// SNMP or gNMI collector; the poller only depends on the shape of the reading.
public interface IResourceSampler
{
    ResourceReading Sample(Device device, DateTimeOffset at);

    IReadOnlyList<InterfaceReading> SampleInterfaces(Device device, IReadOnlyList<DeviceInterface> interfaces, DateTimeOffset at);
}
