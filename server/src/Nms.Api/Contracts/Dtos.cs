using Nms.Api.Domain;

namespace Nms.Api.Contracts;

public record DeviceSummary(
    Guid Id,
    string Name,
    string Hostname,
    string IpAddress,
    DeviceType DeviceType,
    string? Vendor,
    string? Model,
    string Site,
    string? Building,
    string? Room,
    DeviceStatus Status,
    bool IsMonitored,
    int PollIntervalSeconds,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? StatusChangedAt,
    long UptimeSeconds,
    IReadOnlyList<string> Tags,
    double? LatencyMs,
    double? PacketLossPct,
    double? CpuPct,
    double? MemoryPct,
    double? InMbps,
    double? OutMbps,
    int OpenAlerts);

public record DeviceDetail(
    DeviceSummary Device,
    string? Notes,
    string? OsVersion,
    string? ManagementUrl,
    IReadOnlyList<InterfaceSummary> Interfaces,
    IReadOnlyList<NeighborSummary> Neighbors,
    double? AvailabilityPct24h,
    double? AvgLatencyMs24h);

public record InterfaceSummary(
    Guid Id,
    int IfIndex,
    string Name,
    string? Description,
    int SpeedMbps,
    bool AdminUp,
    bool OperUp,
    double? UtilizationPct,
    double? InMbps,
    double? OutMbps,
    long Errors);

public record NeighborSummary(Guid LinkId, Guid DeviceId, string Name, DeviceStatus Status, LinkType LinkType, int CapacityMbps, LinkStatus LinkStatus);

public record SaveDeviceRequest(
    string Name,
    string Hostname,
    string IpAddress,
    DeviceType DeviceType,
    string? Vendor,
    string? Model,
    string? OsVersion,
    string Site,
    string? Building,
    string? Room,
    string? ManagementUrl,
    int PollIntervalSeconds,
    bool IsMonitored,
    List<string>? Tags,
    string? Notes);

public record SaveInterfaceRequest(int IfIndex, string Name, string? Description, int SpeedMbps, bool AdminUp, bool OperUp, string? MacAddress);

public record SaveLinkRequest(Guid ADeviceId, Guid? AInterfaceId, Guid BDeviceId, Guid? BInterfaceId, int CapacityMbps, LinkType LinkType);

public record MaintenanceRequest(bool InMaintenance);

public record TopologyGraph(IReadOnlyList<TopologyNode> Nodes, IReadOnlyList<TopologyEdge> Edges, IReadOnlyList<string> Sites);

public record TopologyNode(
    Guid Id,
    string Name,
    string Hostname,
    DeviceType DeviceType,
    DeviceStatus Status,
    string Site,
    string? Building,
    int Tier,
    int OpenAlerts,
    double? LatencyMs,
    double? CpuPct,
    IReadOnlyList<string> Tags);

public record TopologyEdge(
    Guid Id,
    Guid Source,
    Guid Target,
    LinkType LinkType,
    LinkStatus Status,
    int CapacityMbps,
    double? UtilizationPct,
    double? TrafficMbps);

public record PathHop(int Hop, Guid DeviceId, string Name, DeviceStatus Status, LinkType? IngressLink, double? UtilizationPct);

public record ConnectivityPath(bool Found, IReadOnlyList<PathHop> Hops, string? Message);

public record AlertSummary(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    Guid? InterfaceId,
    string? InterfaceName,
    Guid? RuleId,
    string? RuleName,
    AlertSeverity Severity,
    AlertState State,
    string Title,
    string? Detail,
    string Metric,
    double? ObservedValue,
    double? Threshold,
    DateTimeOffset RaisedAt,
    DateTimeOffset LastSeenAt,
    int Occurrences,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy,
    DateTimeOffset? ResolvedAt);

public record AlertDetail(AlertSummary Alert, IReadOnlyList<AlertEventEntry> Timeline);

public record AlertEventEntry(Guid Id, AlertEventKind Kind, string? Message, string? Actor, DateTimeOffset CreatedAt);

public record AcknowledgeRequest(string? Note);

public record ResolveRequest(string? Note);

public record CommentRequest(string Message);

public record SaveRuleRequest(
    string Name,
    string? Description,
    AlertMetric Metric,
    Comparison Comparison,
    double Threshold,
    int DurationSeconds,
    AlertSeverity Severity,
    RuleTargetKind TargetKind,
    string? TargetValue,
    bool Enabled,
    List<string>? NotifyChannels);

public record DiagnosticRequest(DiagnosticKind Kind, int? Port);

public record DiagnosticSummary(
    Guid Id,
    Guid DeviceId,
    DiagnosticKind Kind,
    DiagnosticState State,
    bool? Success,
    string? Summary,
    string? RequestedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Output);

public record TimeSeriesPoint(DateTimeOffset At, double? Value);

public record NamedSeries(string Name, IReadOnlyList<TimeSeriesPoint> Points);

public record DashboardOverview(
    int TotalDevices,
    int Monitored,
    int Up,
    int Degraded,
    int Down,
    int Maintenance,
    int ActiveAlerts,
    int CriticalAlerts,
    int UnacknowledgedAlerts,
    double AvailabilityPct24h,
    double AvgLatencyMs,
    double PeakThroughputMbps,
    double TotalInMbps,
    double TotalOutMbps,
    DateTimeOffset? LastPollAt,
    IReadOnlyList<SiteRollup> Sites,
    IReadOnlyList<NamedSeries> Throughput,
    IReadOnlyList<NamedSeries> Latency,
    IReadOnlyList<AlertSummary> RecentAlerts,
    IReadOnlyList<DeviceSummary> Watchlist);

public record SiteRollup(string Site, int Devices, int Up, int Degraded, int Down, int ActiveAlerts, double AvailabilityPct);

public record UtilizationRow(
    Guid InterfaceId,
    Guid DeviceId,
    string DeviceName,
    string InterfaceName,
    int SpeedMbps,
    double AvgUtilizationPct,
    double PeakUtilizationPct,
    double AvgInMbps,
    double AvgOutMbps,
    long Errors);

public record UptimeRow(Guid DeviceId, string DeviceName, DeviceType DeviceType, string Site, double AvailabilityPct, double? AvgLatencyMs, double? PeakLatencyMs, int Outages, TimeSpan TotalDowntime);

public record AlertTrendRow(DateTimeOffset Bucket, int Info, int Warning, int Major, int Critical);

public record TopTalkerRow(Guid DeviceId, string DeviceName, double AvgInMbps, double AvgOutMbps, double PeakMbps);

public record ReportWindow(DateTimeOffset From, DateTimeOffset To, string Granularity);

public record UtilizationReport(ReportWindow Window, IReadOnlyList<UtilizationRow> Interfaces, IReadOnlyList<NamedSeries> Series, IReadOnlyList<TopTalkerRow> TopTalkers);

public record AvailabilityReport(ReportWindow Window, IReadOnlyList<UptimeRow> Devices, double FleetAvailabilityPct, IReadOnlyList<NamedSeries> Daily);

public record AlertReport(ReportWindow Window, IReadOnlyList<AlertTrendRow> Trend, IReadOnlyList<RuleNoiseRow> NoisiestRules, IReadOnlyList<DeviceNoiseRow> NoisiestDevices, double MeanTimeToAcknowledgeMinutes, double MeanTimeToResolveMinutes);

public record RuleNoiseRow(Guid? RuleId, string RuleName, int Alerts, int StillOpen);

public record DeviceNoiseRow(Guid DeviceId, string DeviceName, int Alerts, AlertSeverity WorstSeverity);

public record LoginRequest(string Email, string Password);

public record AuthenticatedUser(Guid Id, string Email, string DisplayName, string Role, string Token, DateTimeOffset ExpiresAt);
