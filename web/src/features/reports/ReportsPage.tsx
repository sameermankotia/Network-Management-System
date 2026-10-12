import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { duration, mbps, ms, pct, stamp } from '@/lib/format';
import { Empty, Meter, Segmented, SeverityBadge, Skeleton, Stat } from '@/components/Bits';
import { TimeSeriesChart } from '@/charts/TimeSeriesChart';
import { StackedBars, type StackRow } from '@/charts/StackedBars';
import { RankBars } from '@/charts/RankBars';

const TABS = [
  { label: 'Utilization', value: 'utilization' },
  { label: 'Availability', value: 'availability' },
  { label: 'Alerts', value: 'alerts' }
];

export function ReportsPage() {
  const [tab, setTab] = useState('utilization');

  return (
    <div className="page">
      <div className="toolbar">
        <Segmented options={TABS} value={tab} onChange={setTab} />
        <span className="spacer" />
        <span className="muted">Analytics run straight off the sample tables, so every figure is traceable to a poll.</span>
      </div>

      {tab === 'utilization' && <UtilizationReport />}
      {tab === 'availability' && <AvailabilityReport />}
      {tab === 'alerts' && <AlertAnalytics />}
    </div>
  );
}

function UtilizationReport() {
  const [hours, setHours] = useState(24);
  const { data, isLoading } = useQuery({
    queryKey: ['report-utilization', hours],
    queryFn: () => api.utilizationReport(hours)
  });

  if (isLoading || !data) {
    return <Skeleton height={320} />;
  }

  const busiest = data.interfaces[0];

  return (
    <>
      <div className="grid stats">
        <Stat label="Interfaces sampled" value={data.interfaces.length} foot={<span>window {data.window.granularity} buckets</span>} />
        <Stat
          label="Busiest link"
          value={busiest ? pct(busiest.peakUtilizationPct) : '—'}
          foot={<span>{busiest ? `${busiest.deviceName} ${busiest.interfaceName}` : 'no samples'}</span>}
        />
        <Stat
          label="Links over 70%"
          value={data.interfaces.filter((row) => row.peakUtilizationPct > 70).length}
          foot={<span>peak in window</span>}
        />
        <Stat
          label="Interface errors"
          value={data.interfaces.reduce((sum, row) => sum + row.errors, 0)}
          foot={<span>in + out across the fleet</span>}
        />
      </div>

      <div className="grid halves">
        <section className="card">
          <div className="card-head">
            <h2>Busiest interfaces over time</h2>
            <span className="hint">Average utilization per bucket</span>
            <div className="actions">
              <Segmented
                options={[
                  { label: '6h', value: 6 },
                  { label: '24h', value: 24 },
                  { label: '7d', value: 168 },
                  { label: '30d', value: 720 }
                ]}
                value={hours}
                onChange={setHours}
              />
            </div>
          </div>
          <TimeSeriesChart series={data.series} unit="%" maxHint={100} height={230} byDay={hours > 48} />
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Top talkers</h2>
            <span className="hint">Average combined throughput</span>
          </div>
          <RankBars
            rows={data.topTalkers.map((row) => ({
              label: row.deviceName,
              value: row.avgInMbps + row.avgOutMbps,
              display: mbps(row.avgInMbps + row.avgOutMbps),
              sublabel: `peak ${mbps(row.peakMbps)}`
            }))}
          />
        </section>
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Interface utilization</h2>
          <span className="hint">{stamp(data.window.from)} → {stamp(data.window.to)}</span>
          <div className="actions">
            <a className="btn ghost sm" href={api.exportUrl('utilization', { hours })}>
              Export CSV
            </a>
          </div>
        </div>

        {data.interfaces.length === 0 ? (
          <Empty title="No interface samples in this window" />
        ) : (
          <table>
            <thead>
              <tr>
                <th>Device</th>
                <th>Interface</th>
                <th className="num">Speed</th>
                <th style={{ width: 180 }}>Average</th>
                <th className="num">Peak</th>
                <th className="num">In</th>
                <th className="num">Out</th>
                <th className="num">Errors</th>
              </tr>
            </thead>
            <tbody>
              {data.interfaces.slice(0, 40).map((row) => (
                <tr key={row.interfaceId}>
                  <td>
                    <Link to={`/devices/${row.deviceId}`}>{row.deviceName}</Link>
                  </td>
                  <td className="mono">{row.interfaceName}</td>
                  <td className="num">{row.speedMbps >= 1000 ? `${row.speedMbps / 1000}G` : `${row.speedMbps}M`}</td>
                  <td>
                    <Meter value={row.avgUtilizationPct} warnAt={60} hotAt={80} />
                  </td>
                  <td className="num">{pct(row.peakUtilizationPct)}</td>
                  <td className="num">{mbps(row.avgInMbps)}</td>
                  <td className="num">{mbps(row.avgOutMbps)}</td>
                  <td className="num">{row.errors || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </>
  );
}

function AvailabilityReport() {
  const [days, setDays] = useState(7);
  const { data, isLoading } = useQuery({
    queryKey: ['report-availability', days],
    queryFn: () => api.availabilityReport(days)
  });

  if (isLoading || !data) {
    return <Skeleton height={320} />;
  }

  const worst = data.devices[0];
  const breached = data.devices.filter((row) => row.availabilityPct < 99.9);

  return (
    <>
      <div className="grid stats">
        <Stat label="Fleet availability" value={pct(data.fleetAvailabilityPct, 3)} foot={<span>mean across devices</span>} />
        <Stat
          label="Below 99.9%"
          value={breached.length}
          foot={<span>{breached.length ? 'review these first' : 'everything inside target'}</span>}
        />
        <Stat
          label="Worst device"
          value={worst ? pct(worst.availabilityPct, 2) : '—'}
          foot={<span>{worst?.deviceName ?? 'no samples'}</span>}
        />
        <Stat
          label="Total outages"
          value={data.devices.reduce((sum, row) => sum + row.outages, 0)}
          foot={<span>distinct down transitions</span>}
        />
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Daily availability by site</h2>
          <span className="hint">Percent of polls answered</span>
          <div className="actions">
            <Segmented
              options={[
                { label: '7d', value: 7 },
                { label: '30d', value: 30 },
                { label: '90d', value: 90 }
              ]}
              value={days}
              onChange={setDays}
            />
          </div>
        </div>
        <TimeSeriesChart series={data.daily} unit="%" maxHint={100} height={230} byDay />
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Device uptime</h2>
          <span className="hint">{days} day window</span>
          <div className="actions">
            <a className="btn ghost sm" href={api.exportUrl('availability', { days })}>
              Export CSV
            </a>
          </div>
        </div>

        {data.devices.length === 0 ? (
          <Empty title="No samples in this window" />
        ) : (
          <table>
            <thead>
              <tr>
                <th>Device</th>
                <th>Site</th>
                <th style={{ width: 180 }}>Availability</th>
                <th className="num">Avg latency</th>
                <th className="num">Peak latency</th>
                <th className="num">Outages</th>
                <th className="num">Downtime</th>
              </tr>
            </thead>
            <tbody>
              {data.devices.map((row) => (
                <tr key={row.deviceId}>
                  <td>
                    <Link to={`/devices/${row.deviceId}`}>{row.deviceName}</Link>
                    <div className="muted" style={{ fontSize: 11 }}>{row.deviceType}</div>
                  </td>
                  <td>{row.site}</td>
                  <td>
                    <Meter value={row.availabilityPct} warnAt={99.9} hotAt={99} invert />
                  </td>
                  <td className="num">{ms(row.avgLatencyMs)}</td>
                  <td className="num">{ms(row.peakLatencyMs)}</td>
                  <td className="num">{row.outages || '—'}</td>
                  <td className="num">{row.outages ? duration(row.totalDowntime) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </>
  );
}

function AlertAnalytics() {
  const [days, setDays] = useState(14);
  const granularity = days > 30 ? 'week' : 'day';

  const { data, isLoading } = useQuery({
    queryKey: ['report-alerts', days, granularity],
    queryFn: () => api.alertReport(days, granularity)
  });

  if (isLoading || !data) {
    return <Skeleton height={320} />;
  }

  const rows: StackRow[] = data.trend.map((bucket) => ({
    label: new Date(bucket.bucket).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    segments: [
      { name: 'Critical', value: bucket.critical, color: 'var(--critical)' },
      { name: 'Major', value: bucket.major, color: 'var(--serious)' },
      { name: 'Warning', value: bucket.warning, color: 'var(--warning)' },
      { name: 'Info', value: bucket.info, color: 'var(--ink-muted)' }
    ]
  }));

  const total = data.trend.reduce((sum, row) => sum + row.info + row.warning + row.major + row.critical, 0);

  return (
    <>
      <div className="grid stats">
        <Stat label="Alerts raised" value={total} foot={<span>last {days} days</span>} />
        <Stat
          label="Mean time to acknowledge"
          value={data.meanTimeToAcknowledgeMinutes.toFixed(1)}
          unit="min"
          foot={<span>from raise to pickup</span>}
        />
        <Stat
          label="Mean time to resolve"
          value={data.meanTimeToResolveMinutes.toFixed(1)}
          unit="min"
          foot={<span>includes auto-resolved</span>}
        />
        <Stat
          label="Still open"
          value={data.noisiestRules.reduce((sum, rule) => sum + rule.stillOpen, 0)}
          foot={<span>across all rules</span>}
        />
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Alerts by severity</h2>
          <span className="hint">Grouped by {granularity}</span>
          <div className="actions">
            <Segmented
              options={[
                { label: '14d', value: 14 },
                { label: '30d', value: 30 },
                { label: '90d', value: 90 }
              ]}
              value={days}
              onChange={setDays}
            />
          </div>
        </div>
        <StackedBars rows={rows} height={230} />
      </section>

      <div className="grid halves">
        <section className="card">
          <div className="card-head">
            <h2>Noisiest rules</h2>
            <span className="hint">Candidates for threshold tuning</span>
          </div>
          {data.noisiestRules.length === 0 ? (
            <Empty title="No alerts in this window" />
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Rule</th>
                  <th className="num">Alerts</th>
                  <th className="num">Still open</th>
                </tr>
              </thead>
              <tbody>
                {data.noisiestRules.map((rule) => (
                  <tr key={rule.ruleId ?? rule.ruleName}>
                    <td>{rule.ruleName}</td>
                    <td className="num">{rule.alerts}</td>
                    <td className="num">{rule.stillOpen || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        <section className="card">
          <div className="card-head">
            <h2>Devices raising the most alerts</h2>
            <span className="hint">Recurring problems worth a site visit</span>
          </div>
          {data.noisiestDevices.length === 0 ? (
            <Empty title="No alerts in this window" />
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Device</th>
                  <th>Worst severity</th>
                  <th className="num">Alerts</th>
                </tr>
              </thead>
              <tbody>
                {data.noisiestDevices.map((device) => (
                  <tr key={device.deviceId}>
                    <td>
                      <Link to={`/devices/${device.deviceId}`}>{device.deviceName}</Link>
                    </td>
                    <td>
                      <SeverityBadge severity={device.worstSeverity} />
                    </td>
                    <td className="num">{device.alerts}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </div>
    </>
  );
}
