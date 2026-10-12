import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '@/lib/api';
import { ago, mbps, ms, pct } from '@/lib/format';
import { Empty, SeverityBadge, Segmented, Skeleton, Stat, StatusPill } from '@/components/Bits';
import { TimeSeriesChart } from '@/charts/TimeSeriesChart';
import { useLive } from '@/realtime/LiveProvider';
import { useState } from 'react';

const RANGES = [
  { label: '6h', value: 6 },
  { label: '24h', value: 24 },
  { label: '72h', value: 72 }
];

export function DashboardPage() {
  const [hours, setHours] = useState(6);
  const { lastCycle } = useLive();

  const { data, isLoading } = useQuery({
    queryKey: ['dashboard', hours],
    queryFn: () => api.dashboard(hours),
    refetchInterval: 60_000
  });

  if (isLoading || !data) {
    return (
      <div className="page">
        <div className="grid stats">
          {Array.from({ length: 5 }).map((_, index) => (
            <Skeleton key={index} height={88} />
          ))}
        </div>
        <Skeleton height={240} />
      </div>
    );
  }

  const liveIn = lastCycle?.totalInMbps ?? data.totalInMbps;
  const liveOut = lastCycle?.totalOutMbps ?? data.totalOutMbps;

  return (
    <div className="page">
      <div className="grid stats">
        <Stat
          label="Devices up"
          value={`${data.up}`}
          unit={`/ ${data.monitored}`}
          foot={
            <>
              {data.degraded > 0 && <StatusPill status="Degraded" label={`${data.degraded} degraded`} />}
              {data.down > 0 && <StatusPill status="Down" label={`${data.down} down`} pulse />}
              {data.degraded === 0 && data.down === 0 && <StatusPill status="Up" label="All responding" />}
            </>
          }
        />
        <Stat
          label="Open alerts"
          value={data.activeAlerts}
          foot={
            data.activeAlerts === 0 ? (
              <span>Queue is clear</span>
            ) : (
              <span>
                {data.criticalAlerts} critical · {data.unacknowledgedAlerts} unacknowledged
              </span>
            )
          }
        />
        <Stat label="Availability" value={pct(data.availabilityPct24h, 2)} foot={<span>last 24 hours</span>} />
        <Stat label="Average latency" value={ms(data.avgLatencyMs)} foot={<span>fleet, last 24 hours</span>} />
        <Stat
          label="Traffic now"
          value={mbps(liveIn + liveOut)}
          foot={
            <span>
              ↓ {mbps(liveIn)} · ↑ {mbps(liveOut)}
            </span>
          }
        />
        <Stat
          label="Last poll"
          value={ago(lastCycle?.at ?? data.lastPollAt)}
          foot={<span>{lastCycle ? `${lastCycle.devicesPolled} devices in ${lastCycle.durationMs} ms` : 'waiting for poller'}</span>}
        />
      </div>

      <div className="grid halves">
        <section className="card">
          <div className="card-head">
            <h2>Fleet throughput</h2>
            <span className="hint">Mbps across all monitored devices</span>
            <div className="actions">
              <Segmented options={RANGES} value={hours} onChange={setHours} />
            </div>
          </div>
          <TimeSeriesChart series={data.throughput} unit="Mbps" area height={210} />
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Round trip latency</h2>
            <span className="hint">Average and peak, milliseconds</span>
          </div>
          <TimeSeriesChart series={data.latency} unit="ms" height={210} />
        </section>
      </div>

      <div className="grid halves">
        <section className="card">
          <div className="card-head">
            <h2>Open alerts</h2>
            <span className="hint">Worst first</span>
            <div className="actions">
              <Link className="btn ghost sm" to="/alerts">
                Open queue
              </Link>
            </div>
          </div>

          {data.recentAlerts.length === 0 ? (
            <Empty title="Nothing is alerting" hint="Rules are armed and every metric is inside its threshold." />
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Severity</th>
                  <th>Alert</th>
                  <th>Device</th>
                  <th className="num">Raised</th>
                </tr>
              </thead>
              <tbody>
                {data.recentAlerts.map((alert) => (
                  <tr key={alert.id}>
                    <td>
                      <SeverityBadge severity={alert.severity} />
                    </td>
                    <td>
                      <div>{alert.title}</div>
                      {alert.state === 'Acknowledged' && <span className="muted">acknowledged by {alert.acknowledgedBy}</span>}
                    </td>
                    <td>
                      <Link to={`/devices/${alert.deviceId}`}>{alert.deviceName}</Link>
                    </td>
                    <td className="num muted">{ago(alert.raisedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Needs attention</h2>
            <span className="hint">Ranked by state, then alert count</span>
          </div>

          <table>
            <thead>
              <tr>
                <th>Device</th>
                <th>State</th>
                <th className="num">Latency</th>
                <th className="num">CPU</th>
                <th className="num">Alerts</th>
              </tr>
            </thead>
            <tbody>
              {data.watchlist.map((device) => (
                <tr key={device.id}>
                  <td>
                    <Link to={`/devices/${device.id}`}>{device.name}</Link>
                    <div className="mono">{device.hostname}</div>
                  </td>
                  <td>
                    <StatusPill status={device.status} pulse />
                  </td>
                  <td className="num">{ms(device.latencyMs)}</td>
                  <td className="num">{pct(device.cpuPct)}</td>
                  <td className="num">{device.openAlerts || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Sites</h2>
          <span className="hint">Availability over the last 24 hours</span>
        </div>
        <table>
          <thead>
            <tr>
              <th>Site</th>
              <th className="num">Devices</th>
              <th className="num">Up</th>
              <th className="num">Degraded</th>
              <th className="num">Down</th>
              <th className="num">Open alerts</th>
              <th className="num">Availability</th>
            </tr>
          </thead>
          <tbody>
            {data.sites.map((site) => (
              <tr key={site.site}>
                <td>{site.site}</td>
                <td className="num">{site.devices}</td>
                <td className="num">{site.up}</td>
                <td className="num">{site.degraded || '—'}</td>
                <td className="num">{site.down || '—'}</td>
                <td className="num">{site.activeAlerts || '—'}</td>
                <td className="num">{pct(site.availabilityPct, 2)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
