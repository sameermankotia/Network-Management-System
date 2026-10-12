namespace Nms.Api.Domain;

public enum DeviceType
{
    Router,
    Switch,
    Firewall,
    AccessPoint,
    Server,
    Ups,
    Printer
}

public enum DeviceStatus
{
    Unknown,
    Up,
    Degraded,
    Down,
    Maintenance
}

public enum LinkType
{
    Ethernet,
    Fiber,
    Wireless,
    Virtual
}

public enum LinkStatus
{
    Unknown,
    Up,
    Degraded,
    Down
}

public enum AlertMetric
{
    Reachability,
    Latency,
    PacketLoss,
    Cpu,
    Memory,
    InterfaceUtilization
}

public enum Comparison
{
    GreaterThan,
    LessThan
}

public enum AlertSeverity
{
    Info,
    Warning,
    Major,
    Critical
}

public enum AlertState
{
    Active,
    Acknowledged,
    Resolved
}

public enum AlertEventKind
{
    Raised,
    Reoccurred,
    Acknowledged,
    Commented,
    Escalated,
    Resolved,
    Notified
}

public enum RuleTargetKind
{
    All,
    Device,
    DeviceType,
    Tag,
    Site
}

public enum DiagnosticKind
{
    Ping,
    TcpCheck,
    TraceRoute,
    HealthCheck
}

public enum DiagnosticState
{
    Running,
    Completed,
    Failed
}
