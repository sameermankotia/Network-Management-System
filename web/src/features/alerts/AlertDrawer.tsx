import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { ago, metricLabel, metricUnit, stamp } from '@/lib/format';
import { Drawer, SeverityBadge, Skeleton } from '@/components/Bits';
import { useToasts } from '@/components/Toasts';

export function AlertDrawer({ alertId, onClose }: { alertId: string; onClose: () => void }) {
  const queryClient = useQueryClient();
  const { push } = useToasts();
  const [note, setNote] = useState('');

  const { data, isLoading } = useQuery({
    queryKey: ['alert', alertId],
    queryFn: () => api.alert(alertId)
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['alert', alertId] });
    queryClient.invalidateQueries({ queryKey: ['alerts'] });
    queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const acknowledge = useMutation({
    mutationFn: () => api.acknowledge(alertId, note || undefined),
    onSuccess: () => {
      push({ title: 'Alert acknowledged', tone: 'good' });
      setNote('');
      invalidate();
    }
  });

  const resolve = useMutation({
    mutationFn: () => api.resolve(alertId, note || undefined),
    onSuccess: () => {
      push({ title: 'Alert resolved', tone: 'good' });
      setNote('');
      invalidate();
      onClose();
    }
  });

  const comment = useMutation({
    mutationFn: () => api.comment(alertId, note),
    onSuccess: () => {
      setNote('');
      invalidate();
    }
  });

  const alert = data?.alert;

  return (
    <Drawer
      title={alert?.title ?? 'Alert'}
      subtitle={alert ? `${alert.deviceName} · raised ${ago(alert.raisedAt)}` : undefined}
      onClose={onClose}
      footer={
        alert && (
          <>
            <button className="btn" disabled={!note.trim() || comment.isPending} onClick={() => comment.mutate()}>
              Add note
            </button>
            <button
              className="btn"
              disabled={alert.state !== 'Active' || acknowledge.isPending}
              onClick={() => acknowledge.mutate()}
            >
              Acknowledge
            </button>
            <button
              className="btn primary"
              disabled={alert.state === 'Resolved' || resolve.isPending}
              onClick={() => resolve.mutate()}
            >
              Resolve
            </button>
          </>
        )
      }
    >
      {isLoading || !alert ? (
        <Skeleton height={220} />
      ) : (
        <>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <SeverityBadge severity={alert.severity} />
            <span className="chip">{alert.state}</span>
            {alert.occurrences > 1 && <span className="chip">{alert.occurrences} polls</span>}
          </div>

          {alert.detail && <p style={{ margin: 0, color: 'var(--ink-secondary)' }}>{alert.detail}</p>}

          <dl className="kv">
            <dt>Device</dt>
            <dd>
              <Link to={`/devices/${alert.deviceId}`} onClick={onClose}>
                {alert.deviceName}
              </Link>
            </dd>
            {alert.interfaceName && (
              <>
                <dt>Interface</dt>
                <dd className="mono">{alert.interfaceName}</dd>
              </>
            )}
            <dt>Rule</dt>
            <dd>{alert.ruleName ?? '—'}</dd>
            <dt>Metric</dt>
            <dd>{metricLabel(alert.metric)}</dd>
            <dt>Observed</dt>
            <dd>
              {alert.observedValue?.toFixed(2) ?? '—'} {metricUnit(alert.metric)}
              {alert.threshold !== null && alert.threshold !== undefined && (
                <span className="muted"> (threshold {alert.threshold} {metricUnit(alert.metric)})</span>
              )}
            </dd>
            <dt>First seen</dt>
            <dd>{stamp(alert.raisedAt)}</dd>
            <dt>Last seen</dt>
            <dd>{stamp(alert.lastSeenAt)}</dd>
            {alert.acknowledgedBy && (
              <>
                <dt>Acknowledged</dt>
                <dd>
                  {alert.acknowledgedBy} · {stamp(alert.acknowledgedAt)}
                </dd>
              </>
            )}
            {alert.resolvedAt && (
              <>
                <dt>Resolved</dt>
                <dd>{stamp(alert.resolvedAt)}</dd>
              </>
            )}
          </dl>

          <div className="field">
            <label htmlFor="note">Note</label>
            <textarea
              id="note"
              rows={3}
              value={note}
              placeholder="What did you find? This is kept on the alert timeline."
              onChange={(event) => setNote(event.target.value)}
            />
          </div>

          <div>
            <div className="card-head">
              <h2>Timeline</h2>
            </div>
            <div className="timeline">
              {data.timeline.map((entry) => (
                <div className="timeline-item" key={entry.id}>
                  <span className={`timeline-dot ${entry.kind.toLowerCase()}`} />
                  <div>
                    <div style={{ display: 'flex', gap: 8, alignItems: 'baseline' }}>
                      <strong style={{ fontSize: 12 }}>{entry.kind}</strong>
                      <span className="muted" style={{ fontSize: 11 }}>
                        {entry.actor ?? 'system'} · {stamp(entry.createdAt)}
                      </span>
                    </div>
                    {entry.message && <div style={{ fontSize: 13 }}>{entry.message}</div>}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </>
      )}
    </Drawer>
  );
}
