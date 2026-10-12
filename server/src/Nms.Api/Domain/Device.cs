using System.Net;

namespace Nms.Api.Domain;

public class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Hostname { get; set; } = "";
    public IPAddress IpAddress { get; set; } = IPAddress.None;
    public DeviceType DeviceType { get; set; }
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? OsVersion { get; set; }
    public string Site { get; set; } = "Main Campus";
    public string? Building { get; set; }
    public string? Room { get; set; }
    public string? ManagementUrl { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public bool IsMonitored { get; set; } = true;
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? StatusChangedAt { get; set; }
    public long UptimeSeconds { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<DeviceInterface> Interfaces { get; set; } = [];

    public bool IsDueForPoll(DateTimeOffset now, DateTimeOffset? lastPolledAt) =>
        IsMonitored
        && Status != DeviceStatus.Maintenance
        && (lastPolledAt is null || now - lastPolledAt >= TimeSpan.FromSeconds(PollIntervalSeconds));
}

public class DeviceInterface
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public int IfIndex { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SpeedMbps { get; set; } = 1000;
    public bool AdminUp { get; set; } = true;
    public bool OperUp { get; set; } = true;
    public string? MacAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Device? Device { get; set; }
}

public class Link
{
    public Guid Id { get; set; }
    public Guid ADeviceId { get; set; }
    public Guid? AInterfaceId { get; set; }
    public Guid BDeviceId { get; set; }
    public Guid? BInterfaceId { get; set; }
    public int CapacityMbps { get; set; } = 1000;
    public LinkType LinkType { get; set; } = LinkType.Ethernet;
    public LinkStatus Status { get; set; } = LinkStatus.Unknown;
    public DateTimeOffset CreatedAt { get; set; }

    public Device? ADevice { get; set; }
    public Device? BDevice { get; set; }
}
