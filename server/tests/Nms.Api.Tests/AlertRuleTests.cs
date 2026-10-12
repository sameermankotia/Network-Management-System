using Nms.Api.Domain;
using Xunit;

namespace Nms.Api.Tests;

public class AlertRuleTests
{
    private static Device Device(DeviceType type = DeviceType.Switch, string site = "Main Campus", params string[] tags) =>
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Engineering Dist",
            Hostname = "eng-dist-01",
            DeviceType = type,
            Site = site,
            Tags = [.. tags]
        };

    [Fact]
    public void AllScopedRuleMatchesEveryDevice()
    {
        var rule = new AlertRule { TargetKind = RuleTargetKind.All };

        Assert.True(rule.AppliesTo(Device()));
        Assert.True(rule.AppliesTo(Device(DeviceType.Router)));
    }

    [Fact]
    public void DeviceScopedRuleMatchesOnlyThatDevice()
    {
        var device = Device();
        var rule = new AlertRule { TargetKind = RuleTargetKind.Device, TargetValue = device.Id.ToString() };

        Assert.True(rule.AppliesTo(device));
        Assert.False(rule.AppliesTo(new Device { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void TypeScopedRuleIgnoresCasing()
    {
        var rule = new AlertRule { TargetKind = RuleTargetKind.DeviceType, TargetValue = "switch" };

        Assert.True(rule.AppliesTo(Device()));
        Assert.False(rule.AppliesTo(Device(DeviceType.Firewall)));
    }

    [Fact]
    public void TagScopedRuleMatchesAnyTag()
    {
        var rule = new AlertRule { TargetKind = RuleTargetKind.Tag, TargetValue = "Critical" };

        Assert.True(rule.AppliesTo(Device(DeviceType.Router, "Main Campus", "core", "critical")));
        Assert.False(rule.AppliesTo(Device(DeviceType.Router, "Main Campus", "access")));
    }

    [Fact]
    public void SiteScopedRuleMatchesSite()
    {
        var rule = new AlertRule { TargetKind = RuleTargetKind.Site, TargetValue = "Main Campus" };

        Assert.True(rule.AppliesTo(Device()));
        Assert.False(rule.AppliesTo(Device(DeviceType.Switch, "North Campus")));
    }

    [Fact]
    public void MisconfiguredTargetMatchesNothing()
    {
        var rule = new AlertRule { TargetKind = RuleTargetKind.Device, TargetValue = "not-a-guid" };

        Assert.False(rule.AppliesTo(Device()));
    }

    [Theory]
    [InlineData(Comparison.GreaterThan, 80, 81, true)]
    [InlineData(Comparison.GreaterThan, 80, 80, false)]
    [InlineData(Comparison.LessThan, 1, 0, true)]
    [InlineData(Comparison.LessThan, 1, 1, false)]
    public void ThresholdsAreExclusive(Comparison comparison, double threshold, double observed, bool breached)
    {
        var rule = new AlertRule { Comparison = comparison, Threshold = threshold };

        Assert.Equal(breached, rule.IsBreached(observed));
    }
}
