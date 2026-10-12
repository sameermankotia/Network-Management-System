using System.Collections.Concurrent;
using Nms.Api.Contracts;
using Nms.Api.Domain;

namespace Nms.Api.Monitoring;

/// Last reading per device and per interface, so list and dashboard requests do
/// not fan out into one "latest sample" query per device.
public class LiveTelemetryCache
{
    private readonly ConcurrentDictionary<Guid, DeviceSample> _devices = new();
    private readonly ConcurrentDictionary<Guid, InterfaceReading> _interfaces = new();

    public CycleCompleted? LastCycle { get; private set; }

    public void Record(DeviceSample sample) => _devices[sample.DeviceId] = sample;

    public void Record(IEnumerable<InterfaceReading> readings)
    {
        foreach (var reading in readings)
        {
            _interfaces[reading.InterfaceId] = reading;
        }
    }

    public void RecordCycle(CycleCompleted cycle) => LastCycle = cycle;

    public DeviceSample? Device(Guid deviceId) => _devices.GetValueOrDefault(deviceId);

    public InterfaceReading? Interface(Guid interfaceId) => _interfaces.GetValueOrDefault(interfaceId);

    public IReadOnlyCollection<DeviceSample> Devices() => _devices.Values.ToList();

    public void Forget(Guid deviceId) => _devices.TryRemove(deviceId, out _);
}
