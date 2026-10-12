namespace Nms.Api.Domain;

public class AlertRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public AlertMetric Metric { get; set; }
    public Comparison Comparison { get; set; }
    public double Threshold { get; set; }
    public int DurationSeconds { get; set; }
    public AlertSeverity Severity { get; set; }
    public RuleTargetKind TargetKind { get; set; } = RuleTargetKind.All;
    public string? TargetValue { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> NotifyChannels { get; set; } = ["inapp"];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool AppliesTo(Device device) => TargetKind switch
    {
        RuleTargetKind.All => true,
        RuleTargetKind.Device => Guid.TryParse(TargetValue, out var id) && id == device.Id,
        RuleTargetKind.DeviceType => Enum.TryParse<DeviceType>(TargetValue, true, out var type) && type == device.DeviceType,
        RuleTargetKind.Tag => TargetValue is not null && device.Tags.Contains(TargetValue, StringComparer.OrdinalIgnoreCase),
        RuleTargetKind.Site => string.Equals(TargetValue, device.Site, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public bool IsBreached(double value) =>
        Comparison == Comparison.GreaterThan ? value > Threshold : value < Threshold;
}

public class Alert
{
    public Guid Id { get; set; }
    public Guid? RuleId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? InterfaceId { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertState State { get; set; } = AlertState.Active;
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public string Metric { get; set; } = "";
    public double? ObservedValue { get; set; }
    public double? Threshold { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public int Occurrences { get; set; } = 1;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }

    public Device? Device { get; set; }
    public AlertRule? Rule { get; set; }
    public List<AlertEvent> Events { get; set; } = [];
}

public class AlertEvent
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public AlertEventKind Kind { get; set; }
    public string? Message { get; set; }
    public string? Actor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DiagnosticRun
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public DiagnosticKind Kind { get; set; }
    public DiagnosticState State { get; set; } = DiagnosticState.Running;
    public string? RequestedBy { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool? Success { get; set; }
    public string? Summary { get; set; }
    public string Output { get; set; } = "{}";

    public Device? Device { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "operator";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
