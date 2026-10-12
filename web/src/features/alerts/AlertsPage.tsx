import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { ago, metricLabel, metricUnit } from '@/lib/format';
import { Empty, Segmented, SeverityBadge, Skeleton, Stat } from '@/components/Bits';
import { useToasts } from '@/components/Toasts';
import { AlertDrawer } from './AlertDrawer';
import type { AlertSeverity } from '@/lib/types';

const STATES = [
  { label: 'Open', value: '' },
  { label: 'Active', value: 'Active' },
  { label: 'Acknowledged', value: 'Acknowledged' },
  { label: 'Resolved', value: 'Resolved' }
];

const SEVERITIES: AlertSeverity[] = ['Critical', 'Major', 'Warning', 'Info'];

export function AlertsPage() {
  const queryClient = useQueryClient();
  const { push } = useToasts();

  const [state, setState] = useState('');
  const [severity, setSeverity] = useState('');
  const [hours, setHours] = useState(0);
  const [selected, setSelected] = useState<string[]>([]);
  const [open, setOpen] = useState<string | null>(null);

  const { data: alerts, isLoading } = useQuery({
    queryKey: ['alerts', { state, severity, hours }],
    queryFn: () => api.alerts({ state, severity, hours: hours || undefined, limit: 300 }),
    refetchInterval: 20_000
  });

  const acknowledgeMany = useMutation({
    mutationFn: (ids: string[]) => api.acknowledgeMany(ids),
    onSuccess: (result) => {
      push({ title: `${result.acknowledged} alert(s) acknowledged`, tone: 'good' });
      setSelected([]);
      queryClient.invalidateQueries({ queryKey: ['alerts'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    }
  });

  const rows = alerts ?? [];
  const counts = {
    critical: rows.filter((alert) => alert.severity === 'Critical').length,
    major: rows.filter((alert) => alert.severity === 'Major').length,
    unacknowledged: rows.filter((alert) => alert.state === 'Active').length
  };

  const toggle = (id: string) =>
    setSelected((current) => (current.includes(id) ? current.filter((value) => value !== id) : [...current, id]));

  return (
    <div className="page">
      <div className="grid stats">
        <Stat label="In view" value={rows.length} foot={<span>{state || 'open'} alerts</span>} />
        <Stat label="Critical" value={counts.critical} foot={<span>page the on-call engineer</span>} />
        <Stat label="Major" value={counts.major} foot={<span>needs an owner today</span>} />
        <Stat label="Unacknowledged" value={counts.unacknowledged} foot={<span>nobody has picked these up</span>} />
      </div>

      <div className="toolbar">
        <Segmented options={STATES} value={state} onChange={setState} />

        <select value={severity} onChange={(event) => setSeverity(event.target.value)}>
          <option value="">Any severity</option>
          {SEVERITIES.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>

        <Segmented
          options={[
            { label: 'All time', value: 0 },
            { label: '24h', value: 24 },
            { label: '7d', value: 168 }
          ]}
          value={hours}
          onChange={setHours}
        />

        <span className="spacer" />

        {selected.length > 0 && (
          <button className="btn primary" onClick={() => acknowledgeMany.mutate(selected)}>
            Acknowledge {selected.length}
          </button>
        )}
      </div>

      <section className="card" style={{ padding: 0 }}>
        {isLoading ? (
          <div style={{ padding: 16 }}>
            <Skeleton height={240} />
          </div>
        ) : rows.length === 0 ? (
          <Empty title="Queue is clear" hint="No alerts match this filter." />
        ) : (
          <div style={{ padding: '14px 4px 4px' }}>
            <table>
              <thead>
                <tr>
                  <th style={{ width: 28 }} />
                  <th>Severity</th>
                  <th>Alert</th>
                  <th>Device</th>
                  <th>Metric</th>
                  <th className="num">Observed</th>
                  <th className="num">Polls</th>
                  <th>State</th>
                  <th className="num">Raised</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((alert) => (
                  <tr key={alert.id} className="clickable" onClick={() => setOpen(alert.id)}>
                    <td onClick={(event) => event.stopPropagation()}>
                      <input
                        type="checkbox"
                        checked={selected.includes(alert.id)}
                        disabled={alert.state !== 'Active'}
                        onChange={() => toggle(alert.id)}
                      />
                    </td>
                    <td>
                      <SeverityBadge severity={alert.severity} />
                    </td>
                    <td>
                      <div>{alert.title}</div>
                      {alert.ruleName && <span className="muted" style={{ fontSize: 11 }}>{alert.ruleName}</span>}
                    </td>
                    <td onClick={(event) => event.stopPropagation()}>
                      <Link to={`/devices/${alert.deviceId}`}>{alert.deviceName}</Link>
                      {alert.interfaceName && <div className="mono">{alert.interfaceName}</div>}
                    </td>
                    <td>{metricLabel(alert.metric)}</td>
                    <td className="num">
                      {alert.observedValue?.toFixed(1) ?? '—'} {metricUnit(alert.metric)}
                    </td>
                    <td className="num">{alert.occurrences}</td>
                    <td>
                      {alert.state}
                      {alert.acknowledgedBy && (
                        <div className="muted" style={{ fontSize: 11 }}>by {alert.acknowledgedBy}</div>
                      )}
                    </td>
                    <td className="num muted">{ago(alert.raisedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {open && <AlertDrawer alertId={open} onClose={() => setOpen(null)} />}
    </div>
  );
}
