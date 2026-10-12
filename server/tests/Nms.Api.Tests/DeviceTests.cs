using Nms.Api.Domain;
using Xunit;

namespace Nms.Api.Tests;

public class DeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverPolledDeviceIsDue()
    {
        var device = new Device { PollIntervalSeconds = 60 };

        Assert.True(device.IsDueForPoll(Now, null));
    }

    [Fact]
    public void DeviceIsDueOnceTheIntervalHasElapsed()
    {
        var device = new Device { PollIntervalSeconds = 60 };

        Assert.False(device.IsDueForPoll(Now, Now.AddSeconds(-30)));
        Assert.True(device.IsDueForPoll(Now, Now.AddSeconds(-60)));
    }

    [Fact]
    public void UnmonitoredAndMaintenanceDevicesAreSkipped()
    {
        Assert.False(new Device { IsMonitored = false }.IsDueForPoll(Now, null));
        Assert.False(new Device { Status = DeviceStatus.Maintenance }.IsDueForPoll(Now, null));
    }
}
