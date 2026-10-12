const number = (value: number, digits = 1) =>
  value.toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits });

export function mbps(value?: number | null) {
  if (value === null || value === undefined) return '—';
  if (value >= 1000) return `${number(value / 1000, 2)} Gbps`;
  if (value >= 10) return `${number(value, 0)} Mbps`;
  return `${number(value, 2)} Mbps`;
}

export function ms(value?: number | null) {
  if (value === null || value === undefined) return '—';
  return `${number(value, value < 10 ? 2 : 0)} ms`;
}

export function pct(value?: number | null, digits = 1) {
  if (value === null || value === undefined) return '—';
  return `${number(value, digits)}%`;
}

export function uptime(seconds: number) {
  if (!seconds) return '—';
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  if (days) return `${days}d ${hours}h`;
  if (hours) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

export function duration(iso: string) {
  // The API sends TimeSpan as d.hh:mm:ss or hh:mm:ss
  const [left, right] = iso.includes('.') ? iso.split('.') : ['0', iso];
  const [h = '0', m = '0'] = (right ?? '').split(':');
  const days = Number(left);
  const hours = Number(h);
  const minutes = Number(m);
  if (days) return `${days}d ${hours}h`;
  if (hours) return `${hours}h ${minutes}m`;
  return `${minutes}m`;
}

export function clock(value?: string | null) {
  if (!value) return '—';
  return new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

export function stamp(value?: string | null) {
  if (!value) return '—';
  return new Date(value).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

export function ago(value?: string | null) {
  if (!value) return 'never';
  const seconds = Math.max(0, (Date.now() - new Date(value).getTime()) / 1000);
  if (seconds < 10) return 'just now';
  if (seconds < 60) return `${Math.round(seconds)}s ago`;
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h ago`;
  return `${Math.round(seconds / 86400)}d ago`;
}

export function deviceTypeLabel(type: string) {
  return type === 'AccessPoint' ? 'Access point' : type === 'Ups' ? 'UPS' : type;
}

export function metricLabel(metric: string) {
  switch (metric) {
    case 'PacketLoss':
      return 'Packet loss';
    case 'InterfaceUtilization':
      return 'Interface utilization';
    case 'Cpu':
      return 'CPU';
    default:
      return metric;
  }
}

export function metricUnit(metric: string) {
  return metric === 'Latency' ? 'ms' : metric === 'Reachability' ? '' : '%';
}
