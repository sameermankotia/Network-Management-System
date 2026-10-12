using Nms.Api.Alerting;
using Xunit;

namespace Nms.Api.Tests;

public class BreachTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Rule = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();

    [Fact]
    public void FirstObservationHasNoElapsedTime()
    {
        var tracker = new BreachTracker();

        Assert.Equal(TimeSpan.Zero, tracker.Observe(new BreachKey(Rule, DeviceId, null), Start));
    }

    [Fact]
    public void ElapsedTimeIsMeasuredFromTheFirstBreach()
    {
        var tracker = new BreachTracker();
        var key = new BreachKey(Rule, DeviceId, null);

        tracker.Observe(key, Start);

        Assert.Equal(TimeSpan.FromMinutes(2), tracker.Observe(key, Start.AddMinutes(2)));
    }

    [Fact]
    public void ClearingRestartsTheClock()
    {
        var tracker = new BreachTracker();
        var key = new BreachKey(Rule, DeviceId, null);

        tracker.Observe(key, Start);
        tracker.Clear(key);

        Assert.Equal(TimeSpan.Zero, tracker.Observe(key, Start.AddMinutes(5)));
    }

    [Fact]
    public void InterfacesAreTrackedSeparately()
    {
        var tracker = new BreachTracker();
        var first = new BreachKey(Rule, DeviceId, Guid.NewGuid());
        var second = new BreachKey(Rule, DeviceId, Guid.NewGuid());

        tracker.Observe(first, Start);

        Assert.Equal(TimeSpan.Zero, tracker.Observe(second, Start.AddMinutes(3)));
        Assert.Equal(TimeSpan.FromMinutes(3), tracker.Observe(first, Start.AddMinutes(3)));
    }

    [Fact]
    public void ClearDeviceForgetsEveryRuleOnThatDevice()
    {
        var tracker = new BreachTracker();
        var latency = new BreachKey(Rule, DeviceId, null);
        var cpu = new BreachKey(Guid.NewGuid(), DeviceId, null);

        tracker.Observe(latency, Start);
        tracker.Observe(cpu, Start);
        tracker.ClearDevice(DeviceId);

        Assert.Equal(TimeSpan.Zero, tracker.Observe(latency, Start.AddMinutes(9)));
        Assert.Equal(TimeSpan.Zero, tracker.Observe(cpu, Start.AddMinutes(9)));
    }
}
