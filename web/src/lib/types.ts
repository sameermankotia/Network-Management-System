export type DeviceType = 'Router' | 'Switch' | 'Firewall' | 'AccessPoint' | 'Server' | 'Ups' | 'Printer';
export type DeviceStatus = 'Up' | 'Degraded' | 'Down' | 'Maintenance' | 'Unknown';
export type LinkType = 'Ethernet' | 'Fiber' | 'Wireless' | 'Virtual';
export type LinkStatus = 'Up' | 'Degraded' | 'Down' | 'Unknown';
export type AlertSeverity = 'Info' | 'Warning' | 'Major' | 'Critical';
export type AlertState = 'Active' | 'Acknowledged' | 'Resolved';
export type AlertMetric = 'Reachability' | 'Latency' | 'PacketLoss' | 'Cpu' | 'Memory' | 'InterfaceUtilization';
export type Comparison = 'GreaterThan' | 'LessThan';
export type RuleTargetKind = 'All' | 'Device' | 'DeviceType' | 'Tag' | 'Site';
export type DiagnosticKind = 'Ping' | 'TcpCheck' | 'TraceRoute' | 'HealthCheck';
export type DiagnosticState = 'Running' | 'Completed' | 'Failed';
export type AlertEventKind = 'Raised' | 'Reoccurred' | 'Acknowledged' | 'Commented' | 'Escalated' | 'Resolved' | 'Notified';

export interface Device {
  id: string;
  name: string;
  hostname: string;
  ipAddress: string;
  deviceType: DeviceType;
  vendor?: string | null;
  model?: string | null;
  site: string;
  building?: string | null;
  room?: string | null;
  status: DeviceStatus;
  isMonitored: boolean;
  pollIntervalSeconds: number;
  lastSeenAt?: string | null;
  statusChangedAt?: string | null;
  uptimeSeconds: number;
  tags: string[];
  latencyMs?: number | null;
  packetLossPct?: number | null;
  cpuPct?: number | null;
  memoryPct?: number | null;
  inMbps?: number | null;
  outMbps?: number | null;
  openAlerts: number;
}

export interface DeviceInterface {
  id: string;
  ifIndex: number;
  name: string;
  description?: string | null;
  speedMbps: number;
  adminUp: boolean;
  operUp: boolean;
  utilizationPct?: number | null;
  inMbps?: number | null;
  outMbps?: number | null;
  errors: number;
}

export interface Neighbor {
  linkId: string;
  deviceId: string;
  name: string;
  status: DeviceStatus;
  linkType: LinkType;
  capacityMbps: number;
  linkStatus: LinkStatus;
}

export interface DeviceDetail {
  device: Device;
  notes?: string | null;
  osVersion?: string | null;
  managementUrl?: string | null;
  interfaces: DeviceInterface[];
  neighbors: Neighbor[];
  availabilityPct24h?: number | null;
  avgLatencyMs24h?: number | null;
}

export interface SaveDevicePayload {
  name: string;
  hostname: string;
  ipAddress: string;
  deviceType: DeviceType;
  vendor?: string | null;
  model?: string | null;
  osVersion?: string | null;
  site: string;
  building?: string | null;
  room?: string | null;
  managementUrl?: string | null;
  pollIntervalSeconds: number;
  isMonitored: boolean;
  tags: string[];
  notes?: string | null;
}

export interface TopologyNode {
  id: string;
  name: string;
  hostname: string;
  deviceType: DeviceType;
  status: DeviceStatus;
  site: string;
  building?: string | null;
  tier: number;
  openAlerts: number;
  latencyMs?: number | null;
  cpuPct?: number | null;
  tags: string[];
}

export interface TopologyEdge {
  id: string;
  source: string;
  target: string;
  linkType: LinkType;
  status: LinkStatus;
  capacityMbps: number;
  utilizationPct?: number | null;
  trafficMbps?: number | null;
}

export interface TopologyGraph {
  nodes: TopologyNode[];
  edges: TopologyEdge[];
  sites: string[];
}

export interface PathHop {
  hop: number;
  deviceId: string;
  name: string;
  status: DeviceStatus;
  ingressLink?: LinkType | null;
  utilizationPct?: number | null;
}

export interface ConnectivityPath {
  found: boolean;
  hops: PathHop[];
  message?: string | null;
}

export interface Alert {
  id: string;
  deviceId: string;
  deviceName: string;
  interfaceId?: string | null;
  interfaceName?: string | null;
  ruleId?: string | null;
  ruleName?: string | null;
  severity: AlertSeverity;
  state: AlertState;
  title: string;
  detail?: string | null;
  metric: string;
  observedValue?: number | null;
  threshold?: number | null;
  raisedAt: string;
  lastSeenAt: string;
  occurrences: number;
  acknowledgedAt?: string | null;
  acknowledgedBy?: string | null;
  resolvedAt?: string | null;
}

export interface AlertEventEntry {
  id: string;
  kind: AlertEventKind;
  message?: string | null;
  actor?: string | null;
  createdAt: string;
}

export interface AlertDetail {
  alert: Alert;
  timeline: AlertEventEntry[];
}

export interface AlertRule {
  id: string;
  name: string;
  description?: string | null;
  metric: AlertMetric;
  comparison: Comparison;
  threshold: number;
  durationSeconds: number;
  severity: AlertSeverity;
  targetKind: RuleTargetKind;
  targetValue?: string | null;
  enabled: boolean;
  notifyChannels: string[];
  createdAt: string;
  updatedAt: string;
}

export type SaveRulePayload = Omit<AlertRule, 'id' | 'createdAt' | 'updatedAt'>;

export interface DiagnosticRun {
  id: string;
  deviceId: string;
  kind: DiagnosticKind;
  state: DiagnosticState;
  success?: boolean | null;
  summary?: string | null;
  requestedBy?: string | null;
  startedAt: string;
  completedAt?: string | null;
  output: string;
}

export interface TimeSeriesPoint {
  at: string;
  value: number | null;
}

export interface NamedSeries {
  name: string;
  points: TimeSeriesPoint[];
}

export interface SiteRollup {
  site: string;
  devices: number;
  up: number;
  degraded: number;
  down: number;
  activeAlerts: number;
  availabilityPct: number;
}

export interface DashboardOverview {
  totalDevices: number;
  monitored: number;
  up: number;
  degraded: number;
  down: number;
  maintenance: number;
  activeAlerts: number;
  criticalAlerts: number;
  unacknowledgedAlerts: number;
  availabilityPct24h: number;
  avgLatencyMs: number;
  peakThroughputMbps: number;
  totalInMbps: number;
  totalOutMbps: number;
  lastPollAt?: string | null;
  sites: SiteRollup[];
  throughput: NamedSeries[];
  latency: NamedSeries[];
  recentAlerts: Alert[];
  watchlist: Device[];
}

export interface ReportWindow {
  from: string;
  to: string;
  granularity: string;
}

export interface UtilizationRow {
  interfaceId: string;
  deviceId: string;
  deviceName: string;
  interfaceName: string;
  speedMbps: number;
  avgUtilizationPct: number;
  peakUtilizationPct: number;
  avgInMbps: number;
  avgOutMbps: number;
  errors: number;
}

export interface TopTalkerRow {
  deviceId: string;
  deviceName: string;
  avgInMbps: number;
  avgOutMbps: number;
  peakMbps: number;
}

export interface UtilizationReport {
  window: ReportWindow;
  interfaces: UtilizationRow[];
  series: NamedSeries[];
  topTalkers: TopTalkerRow[];
}

export interface UptimeRow {
  deviceId: string;
  deviceName: string;
  deviceType: DeviceType;
  site: string;
  availabilityPct: number;
  avgLatencyMs?: number | null;
  peakLatencyMs?: number | null;
  outages: number;
  totalDowntime: string;
}

export interface AvailabilityReport {
  window: ReportWindow;
  devices: UptimeRow[];
  fleetAvailabilityPct: number;
  daily: NamedSeries[];
}

export interface AlertTrendRow {
  bucket: string;
  info: number;
  warning: number;
  major: number;
  critical: number;
}

export interface AlertReport {
  window: ReportWindow;
  trend: AlertTrendRow[];
  noisiestRules: { ruleId?: string | null; ruleName: string; alerts: number; stillOpen: number }[];
  noisiestDevices: { deviceId: string; deviceName: string; alerts: number; worstSeverity: AlertSeverity }[];
  meanTimeToAcknowledgeMinutes: number;
  meanTimeToResolveMinutes: number;
}

export interface AuthenticatedUser {
  id: string;
  email: string;
  displayName: string;
  role: string;
  token: string;
  expiresAt: string;
}

export interface CycleSummary {
  at: string;
  devicesPolled: number;
  up: number;
  degraded: number;
  down: number;
  activeAlerts: number;
  totalInMbps: number;
  totalOutMbps: number;
  durationMs: number;
}

export interface LiveSample {
  deviceId: string;
  sampledAt: string;
  reachable: boolean;
  latencyMs?: number | null;
  packetLossPct?: number | null;
  cpuPct?: number | null;
  memoryPct?: number | null;
  inMbps?: number | null;
  outMbps?: number | null;
}

export interface StatusChange {
  deviceId: string;
  name: string;
  status: DeviceStatus;
  previous: DeviceStatus;
  at: string;
}

export interface LinkChange {
  linkId: string;
  source: string;
  target: string;
  status: LinkStatus;
  utilizationPct?: number | null;
}
