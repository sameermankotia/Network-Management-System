using Microsoft.Extensions.Configuration;
using Nms.Api.Domain;
using Nms.Api.Monitoring;
using Xunit;

namespace Nms.Api.Tests;

public class SyntheticTelemetryTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 1, 17, 30, 0, TimeSpan.Zero);

    private static SyntheticTelemetry Telemetry(double outageRate = 0) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monitoring:Synthetic:OutageRate"] = outageRate.ToString()
            })
            .Build());

    private static Device Router() => new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        DeviceType = DeviceType.Router
    };

    [Fact]
    public void ReadingsAreStableForTheSameDeviceAndMinute()
    {
        var telemetry = Telemetry();
        var device = Router();

        Assert.Equal(telemetry.Sample(device, At), telemetry.Sample(device, At));
    }

    [Fact]
    public void ReadingsStayInsidePlausibleBounds()
    {
        var telemetry = Telemetry();
        var device = Router();

        for (var minute = 0; minute < 24 * 60; minute += 7)
        {
            var reading = telemetry.Sample(device, At.AddMinutes(minute));

            Assert.InRange(reading.CpuPct, 1, 99);
            Assert.InRange(reading.MemoryPct, 5, 98);
            Assert.True(reading.InMbps >= 0);
            Assert.True(reading.OutMbps >= 0);
        }
    }

    [Fact]
    public void InterfaceUtilizationNeverExceedsTheLink()
    {
        var telemetry = Telemetry();
        var device = Router();
        var interfaces = new List<DeviceInterface>
        {
            new() { Id = Guid.NewGuid(), DeviceId = device.Id, IfIndex = 1, Name = "TenGigE0/0/0", SpeedMbps = 10000 },
            new() { Id = Guid.NewGuid(), DeviceId = device.Id, IfIndex = 2, Name = "ge-0/0/1", SpeedMbps = 1000 }
        };

        for (var minute = 0; minute < 180; minute += 5)
        {
            foreach (var reading in telemetry.SampleInterfaces(device, interfaces, At.AddMinutes(minute)))
            {
                Assert.InRange(reading.UtilizationPct, 0, 100);
            }
        }
    }

    [Fact]
    public void DisabledInterfacesReportNoTraffic()
    {
        var telemetry = Telemetry();
        var device = Router();
        var shut = new DeviceInterface { Id = Guid.NewGuid(), Name = "ge-0/0/9", SpeedMbps = 1000, OperUp = false };

        var reading = Assert.Single(telemetry.SampleInterfaces(device, [shut], At));

        Assert.Equal(0, reading.InMbps);
        Assert.Equal(0, reading.OutMbps);
        Assert.Equal(0, reading.UtilizationPct);
    }

    [Fact]
    public async Task ProbeReportsUnreachableWhenEverythingIsOutage()
    {
        var result = await Telemetry(outageRate: 1).ProbeAsync(Router(), CancellationToken.None);

        Assert.False(result.Reachable);
        Assert.Equal(100, result.PacketLossPct);
    }

    [Fact]
    public async Task ProbeReportsLatencyWhenReachable()
    {
        var result = await Telemetry().ProbeAsync(Router(), CancellationToken.None);

        Assert.True(result.Reachable);
        Assert.NotNull(result.LatencyMs);
        Assert.InRange(result.LatencyMs!.Value, 0.1, 100);
    }
}
