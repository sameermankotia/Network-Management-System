import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { ago, clock, deviceTypeLabel, mbps, ms, pct, stamp, uptime } from '@/lib/format';
import { Empty, Meter, Segmented, SeverityBadge, Skeleton, Stat, StatusPill } from '@/components/Bits';
import { TimeSeriesChart } from '@/charts/TimeSeriesChart';
import { Sparkline } from '@/charts/Sparkline';
import { useDeviceStream } from '@/realtime/LiveProvider';
import { useAuth } from '@/auth/AuthProvider';
import { useToasts } from '@/components/Toasts';
import { DeviceForm } from './DeviceForm';
import type { DiagnosticKind, NamedSeries, SaveDevicePayload } from '@/lib/types';

const RANGES = [
  { label: '6h', value: 6 },
  { label: '24h', value: 24 },
  { label: '7d', value: 168 }
];

const METRIC_TABS = [
  { label: 'Latency', value: 'Latency' },
  { label: 'Loss', value: 'Loss' },
  { label: 'Traffic', value: 'Traffic' },
  { label: 'Load', value: 'Load' },
  { label: 'Availability', value: 'Availability' }
];

const DIAGNOSTICS: { kind: DiagnosticKind; label: string }[] = [
  { kind: 'HealthCheck', label: 'Health check' },
  { kind: 'Ping', label: 'Ping' },
  { kind: 'TraceRoute', label: 'Trace route' },
  { kind: 'TcpCheck', label: 'TCP 443' }
];

export function DeviceDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { isAdmin } = useAuth();
  const { push } = useToasts();
  const queryClient = useQueryClient();

  const [hours, setHours] = useState(6);
  const [metric, setMetric] = useState('Latency');
  const [editing, setEditing] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);

  const { data: detail, isLoading } = useQuery({
    queryKey: ['device', id],
    queryFn: () => api.device(id),
    refetchInterval: 20_000
  });

  const { data: series } = useQuery({
    queryKey: ['device-series', id, hours],
    queryFn: () => api.deviceSeries(id, hours),
    refetchInterval: 60_000
  });

  const { data: alerts } = useQuery({
    queryKey: ['device-alerts', id],
    queryFn: () => api.deviceAlerts(id)
  });

  const { data: diagnostics } = useQuery({
    queryKey: ['diagnostics', id],
    queryFn: () => api.diagnostics(id)
  });

  const live = useDeviceStream(id);

  const update = useMutation({
    mutationFn: (payload: SaveDevicePayload) => api.updateDevice(id, payload),
    onSuccess: () => {
      push({ title: 'Device updated', tone: 'good' });
      queryClient.invalidateQueries({ queryKey: ['device', id] });
      queryClient.invalidateQueries({ queryKey: ['devices'] });
    }
  });

  const runDiagnostic = useMutation({
    mutationFn: (kind: DiagnosticKind) => api.runDiagnostic(id, kind, kind === 'TcpCheck' ? 443 : undefined),
    onSuccess: (run) => {
      push({
        title: `${run.kind} ${run.success ? 'passed' : 'found problems'}`,
        body: run.summary ?? undefined,
        tone: run.success ? 'good' : 'warning'
      });
      setExpanded(run.id);
      queryClient.invalidateQueries({ queryKey: ['diagnostics', id] });
    }
  });

  const remove = useMutation({
    mutationFn: () => api.deleteDevice(id),
    onSuccess: () => {
      push({ title: 'Device removed', tone: 'info' });
      queryClient.invalidateQueries({ queryKey: ['devices'] });
      navigate('/devices');
    }
  });

  const maintenance = useMutation({
    mutationFn: (on: boolean) => api.setMaintenance(id, on),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['device', id] })
  });

  const charted = useMemo(() => pickSeries(series ?? [], metric), [series, metric]);

  const liveLatency = live.map((sample) => sample.latencyMs ?? null);
  const liveSeries: NamedSeries[] = [
    {
      name: 'Latency',
      points: live.map((sample) => ({ at: sample.sampledAt, value: sample.latencyMs ?? null }))
    }
  ];

  if (isLoading || !detail) {
    return (
      <div className="page">
        <Skeleton height={100} />
        <Skeleton height={260} />
      </div>
    );
  }

  const device = detail.device;

  return (
    <div className="page">
      <section className="card">
        <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <div style={{ minWidth: 260 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <h2 style={{ margin: 0, fontSize: 18 }}>{device.name}</h2>
              <StatusPill status={device.status} pulse />
            </div>
            <div className="mono">
              {device.hostname} · {device.ipAddress}
            </div>
            <div className="muted" style={{ marginTop: 6, fontSize: 12 }}>
              {deviceTypeLabel(device.deviceType)} · {device.vendor} {device.model}
              {detail.osVersion ? ` · ${detail.osVersion}` : ''}
            </div>
            <div style={{ display: 'flex', gap: 6, marginTop: 8, flexWrap: 'wrap' }}>
              {device.tags.map((tag) => (
                <span className="chip" key={tag}>
                  {tag}
                </span>
              ))}
            </div>
          </div>

          <dl className="kv" style={{ flex: 1, minWidth: 260 }}>
            <dt>Location</dt>
            <dd>
              {device.site}
              {device.building ? ` · ${device.building}` : ''}
              {device.room ? ` ${device.room}` : ''}
            </dd>
            <dt>Poll interval</dt>
            <dd>{device.pollIntervalSeconds}s {device.isMonitored ? '' : '(polling paused)'}</dd>
            <dt>Last seen</dt>
            <dd>
              {ago(device.lastSeenAt)} <span className="muted">({stamp(device.lastSeenAt)})</span>
            </dd>
            <dt>State since</dt>
            <dd>{stamp(device.statusChangedAt)}</dd>
            {detail.managementUrl && (
              <>
                <dt>Management</dt>
                <dd>
                  <a href={detail.managementUrl} target="_blank" rel="noreferrer">
                    {detail.managementUrl}
                  </a>
                </dd>
              </>
            )}
          </dl>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginLeft: 'auto' }}>
            {DIAGNOSTICS.map((item) => (
              <button
                key={item.kind}
                className={`btn sm ${item.kind === 'HealthCheck' ? 'primary' : ''}`}
                disabled={runDiagnostic.isPending}
                onClick={() => runDiagnostic.mutate(item.kind)}
              >
                {item.label}
              </button>
            ))}
            {isAdmin && (
              <>
                <button className="btn sm" onClick={() => setEditing(true)}>
                  Edit
                </button>
                <button className="btn sm" onClick={() => maintenance.mutate(device.status !== 'Maintenance')}>
                  {device.status === 'Maintenance' ? 'End maintenance' : 'Maintenance'}
                </button>
                <button
                  className="btn sm danger"
                  onClick={() => {
                    if (window.confirm(`Remove ${device.name} and its history?`)) {
                      remove.mutate();
                    }
                  }}
                >
                  Delete
                </button>
              </>
            )}
          </div>
        </div>
      </section>

      <div className="grid stats">
        <Stat label="Availability" value={pct(detail.availabilityPct24h, 2)} foot={<span>last 24 hours</span>} />
        <Stat label="Latency" value={ms(device.latencyMs)} foot={<span>24h avg {ms(detail.avgLatencyMs24h)}</span>}>
          <Sparkline values={liveLatency} width={120} height={26} />
        </Stat>
        <Stat label="Packet loss" value={pct(device.packetLossPct)} foot={<span>current poll</span>} />
        <Stat label="Uptime" value={uptime(device.uptimeSeconds)} foot={<span>since last outage</span>} />
        <Stat
          label="Traffic"
          value={mbps((device.inMbps ?? 0) + (device.outMbps ?? 0))}
          foot={
            <span>
              ↓ {mbps(device.inMbps)} · ↑ {mbps(device.outMbps)}
            </span>
          }
        />
        <Stat label="Open alerts" value={device.openAlerts} foot={<span>on this device</span>} />
      </div>

      <div className="grid halves">
        <section className="card">
          <div className="card-head">
            <h2>History</h2>
            <div className="actions">
              <Segmented options={METRIC_TABS} value={metric} onChange={setMetric} />
              <Segmented options={RANGES} value={hours} onChange={setHours} />
            </div>
          </div>
          <TimeSeriesChart
            series={charted.series}
            unit={charted.unit}
            area={charted.area}
            height={220}
            maxHint={charted.maxHint}
            byDay={hours > 48}
          />
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Live feed</h2>
            <span className="hint">
              {live.length ? `${live.length} samples streamed · ${clock(live[live.length - 1]?.sampledAt)}` : 'waiting for the next poll'}
            </span>
          </div>
          {live.length < 2 ? (
            <Empty title="Streaming" hint={`This device is polled every ${device.pollIntervalSeconds}s. Samples appear here as they arrive.`} />
          ) : (
            <TimeSeriesChart series={liveSeries} unit="ms" height={220} />
          )}
        </section>
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Interfaces</h2>
          <span className="hint">{detail.interfaces.length} recorded</span>
        </div>
        {detail.interfaces.length === 0 ? (
          <Empty title="No interfaces recorded" hint="Add interfaces to track per-port utilization and errors." />
        ) : (
          <table>
            <thead>
              <tr>
                <th>Interface</th>
                <th>Description</th>
                <th className="num">Speed</th>
                <th>State</th>
                <th style={{ width: 170 }}>Utilization</th>
                <th className="num">In</th>
                <th className="num">Out</th>
                <th className="num">Errors</th>
              </tr>
            </thead>
            <tbody>
              {detail.interfaces.map((iface) => (
                <tr key={iface.id}>
                  <td className="mono">{iface.name}</td>
                  <td>{iface.description ?? '—'}</td>
                  <td className="num">{iface.speedMbps >= 1000 ? `${iface.speedMbps / 1000}G` : `${iface.speedMbps}M`}</td>
                  <td>
                    <StatusPill status={iface.operUp ? 'Up' : iface.adminUp ? 'Down' : 'Maintenance'} label={iface.operUp ? 'up' : iface.adminUp ? 'down' : 'disabled'} />
                  </td>
                  <td>
                    <Meter value={iface.utilizationPct} warnAt={60} hotAt={80} />
                  </td>
                  <td className="num">{mbps(iface.inMbps)}</td>
                  <td className="num">{mbps(iface.outMbps)}</td>
                  <td className="num">{iface.errors || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <div className="grid halves">
        <section className="card">
          <div className="card-head">
            <h2>Neighbors</h2>
            <span className="hint">From recorded links</span>
            <div className="actions">
              <Link className="btn ghost sm" to="/topology">
                View topology
              </Link>
            </div>
          </div>
          {detail.neighbors.length === 0 ? (
            <Empty title="No links recorded" hint="Document links to build the topology graph." />
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Device</th>
                  <th>Link</th>
                  <th className="num">Capacity</th>
                  <th>State</th>
                </tr>
              </thead>
              <tbody>
                {detail.neighbors.map((neighbor) => (
                  <tr key={neighbor.linkId}>
                    <td>
                      <Link to={`/devices/${neighbor.deviceId}`}>{neighbor.name}</Link>
                    </td>
                    <td>{neighbor.linkType}</td>
                    <td className="num">{neighbor.capacityMbps >= 1000 ? `${neighbor.capacityMbps / 1000}G` : `${neighbor.capacityMbps}M`}</td>
                    <td>
                      <StatusPill status={neighbor.linkStatus} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Diagnostics</h2>
            <span className="hint">Most recent runs</span>
          </div>
          {!diagnostics?.length ? (
            <Empty title="No diagnostics yet" hint="Run a health check to capture reachability, load and capacity in one pass." />
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {diagnostics.slice(0, 8).map((run) => (
                <div key={run.id} style={{ border: '1px solid var(--border)', borderRadius: 8, padding: '9px 11px' }}>
                  <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
                    <StatusPill status={run.success ? 'Up' : 'Down'} label={run.kind} />
                    <span className="muted" style={{ fontSize: 12 }}>{run.summary}</span>
                    <button className="btn ghost sm" style={{ marginLeft: 'auto' }} onClick={() => setExpanded(expanded === run.id ? null : run.id)}>
                      {expanded === run.id ? 'Hide' : 'Output'}
                    </button>
                  </div>
                  <div className="muted" style={{ fontSize: 11, marginTop: 3 }}>
                    {stamp(run.startedAt)} · {run.requestedBy ?? 'system'}
                  </div>
                  {expanded === run.id && (
                    <pre
                      className="mono"
                      style={{ marginTop: 8, maxHeight: 220, overflow: 'auto', background: 'var(--plane)', padding: 10, borderRadius: 6 }}
                    >
                      {prettyJson(run.output)}
                    </pre>
                  )}
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Alert history</h2>
          <span className="hint">Last 100 alerts for this device</span>
        </div>
        {!alerts?.length ? (
          <Empty title="No alerts recorded" hint="This device has stayed inside every rule threshold." />
        ) : (
          <table>
            <thead>
              <tr>
                <th>Severity</th>
                <th>Alert</th>
                <th>Rule</th>
                <th>State</th>
                <th className="num">Raised</th>
                <th className="num">Resolved</th>
              </tr>
            </thead>
            <tbody>
              {alerts.map((alert) => (
                <tr key={alert.id}>
                  <td>
                    <SeverityBadge severity={alert.severity} />
                  </td>
                  <td>{alert.title}</td>
                  <td className="muted">{alert.ruleName ?? '—'}</td>
                  <td>{alert.state}</td>
                  <td className="num muted">{stamp(alert.raisedAt)}</td>
                  <td className="num muted">{alert.resolvedAt ? stamp(alert.resolvedAt) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      {editing && (
        <DeviceForm
          existing={detail}
          onClose={() => setEditing(false)}
          onSave={async (payload) => void (await update.mutateAsync(payload))}
        />
      )}
    </div>
  );
}

function pickSeries(all: NamedSeries[], metric: string) {
  const pick = (...names: string[]) =>
    names.map((name) => all.find((series) => series.name === name)).filter((series): series is NamedSeries => !!series);

  switch (metric) {
    case 'Traffic':
      return { series: pick('Inbound', 'Outbound'), unit: 'Mbps', area: true, maxHint: undefined };
    case 'Load':
      return { series: pick('CPU', 'Memory'), unit: '%', area: false, maxHint: 100 };
    case 'Availability':
      return { series: pick('Availability'), unit: '%', area: true, maxHint: 100 };
    case 'Loss':
      return { series: pick('Packet loss'), unit: '%', area: true, maxHint: undefined };
    default:
      return { series: pick('Latency'), unit: 'ms', area: false, maxHint: undefined };
  }
}

function prettyJson(raw: string) {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
