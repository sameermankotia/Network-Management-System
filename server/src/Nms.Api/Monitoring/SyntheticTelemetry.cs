using Nms.Api.Domain;

namespace Nms.Api.Monitoring;

/// Deterministic telemetry so the stack can be demoed, developed and load-tested
/// without a lab full of switches. Every value is a function of (device, minute),
/// which keeps charts smooth across restarts.
public class SyntheticTelemetry(IConfiguration config) : IDeviceProbe, IResourceSampler
{
    private readonly double _outageRate = config.GetValue("Monitoring:Synthetic:OutageRate", 0.02);

    public Task<ProbeResult> ProbeAsync(Device device, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var seed = Noise(device.Id, now, 7);

        if (seed < _outageRate)
        {
            return Task.FromResult(ProbeResult.Unreachable("synthetic"));
        }

        var baseline = device.DeviceType switch
        {
            DeviceType.Router or DeviceType.Firewall => 2.5,
            DeviceType.Switch => 1.8,
            DeviceType.AccessPoint => 12.0,
            DeviceType.Server => 0.9,
            _ => 6.0
        };

        var latency = baseline * (0.8 + Wave(device.Id, now, 3) * 0.6) + seed * 4;
        var loss = seed > 0.985 ? Math.Round((seed - 0.985) * 180, 2) : 0;

        return Task.FromResult(new ProbeResult(true, Math.Round(latency, 2), loss, "synthetic"));
    }

    public ResourceReading Sample(Device device, DateTimeOffset at)
    {
        var load = CampusLoadCurve(at);
        var cpuCeiling = device.DeviceType switch
        {
            DeviceType.Firewall => 70,
            DeviceType.Router => 55,
            DeviceType.Server => 65,
            DeviceType.Switch => 40,
            _ => 30
        };

        var cpu = cpuCeiling * load * (0.7 + Wave(device.Id, at, 11) * 0.5);
        var memory = 35 + 45 * (0.5 + Wave(device.Id, at, 13) * 0.5) * (0.6 + load * 0.4);
        var throughput = ThroughputCeiling(device) * load * (0.55 + Wave(device.Id, at, 5) * 0.45);

        return new ResourceReading(
            Clamp(cpu, 1, 99),
            Clamp(memory, 5, 98),
            Math.Round(throughput, 2),
            Math.Round(throughput * 0.72, 2));
    }

    public IReadOnlyList<InterfaceReading> SampleInterfaces(Device device, IReadOnlyList<DeviceInterface> interfaces, DateTimeOffset at)
    {
        var load = CampusLoadCurve(at);
        var readings = new List<InterfaceReading>(interfaces.Count);

        foreach (var iface in interfaces)
        {
            if (!iface.AdminUp || !iface.OperUp)
            {
                readings.Add(new InterfaceReading(iface.Id, 0, 0, 0, 0, 0));
                continue;
            }

            var pull = 0.25 + Wave(iface.Id, at, 17) * 0.75;
            var isUplink = iface.IfIndex == 1;
            var ceiling = iface.SpeedMbps * (isUplink ? 0.62 : 0.3);
            var inMbps = ceiling * load * pull;
            var outMbps = inMbps * (0.55 + Wave(iface.Id, at, 23) * 0.4);
            var errors = Noise(iface.Id, at, 29) > 0.97 ? (long)(Noise(iface.Id, at, 31) * 40) : 0;

            readings.Add(new InterfaceReading(
                iface.Id,
                Math.Round(inMbps, 2),
                Math.Round(outMbps, 2),
                errors,
                errors / 2,
                Math.Round(Clamp(100.0 * Math.Max(inMbps, outMbps) / iface.SpeedMbps, 0, 100), 2)));
        }

        return readings;
    }

    // Busy mid-morning and mid-afternoon, quiet overnight.
    private static double CampusLoadCurve(DateTimeOffset at)
    {
        var hour = at.ToOffset(TimeSpan.FromHours(-7)).TimeOfDay.TotalHours;
        var morning = Math.Exp(-Math.Pow(hour - 10.5, 2) / 10);
        var afternoon = Math.Exp(-Math.Pow(hour - 15.0, 2) / 12);
        var evening = 0.45 * Math.Exp(-Math.Pow(hour - 21.0, 2) / 8);
        return Clamp(0.12 + 0.75 * (morning + afternoon + evening), 0.08, 1.0);
    }

    private static double ThroughputCeiling(Device device) => device.DeviceType switch
    {
        DeviceType.Router or DeviceType.Firewall => 6200,
        DeviceType.Switch => 1800,
        DeviceType.Server => 420,
        DeviceType.AccessPoint => 320,
        _ => 40
    };

    // Hash-based value noise: stable per (id, minute, salt) and cheap.
    private static double Noise(Guid id, DateTimeOffset at, int salt)
    {
        var minute = at.ToUnixTimeSeconds() / 60;
        var hash = HashCode.Combine(id, minute, salt);
        return (uint)hash / (double)uint.MaxValue;
    }

    private static double Wave(Guid id, DateTimeOffset at, int salt)
    {
        var minutes = at.ToUnixTimeSeconds() / 60.0;
        var phase = (uint)HashCode.Combine(id, salt) / (double)uint.MaxValue * Math.Tau;
        var slow = Math.Sin(minutes / 37.0 + phase);
        var fast = Math.Sin(minutes / 6.5 + phase * 2);
        return Clamp(0.5 + 0.35 * slow + 0.15 * fast, 0, 1);
    }

    private static double Clamp(double value, double min, double max) => Math.Round(Math.Clamp(value, min, max), 2);
}
