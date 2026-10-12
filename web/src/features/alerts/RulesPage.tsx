import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { metricLabel, metricUnit, stamp } from '@/lib/format';
import { Empty, Modal, SeverityBadge, Skeleton } from '@/components/Bits';
import { useAuth } from '@/auth/AuthProvider';
import { useToasts } from '@/components/Toasts';
import type { AlertMetric, AlertRule, AlertSeverity, Comparison, RuleTargetKind, SaveRulePayload } from '@/lib/types';

const METRICS: AlertMetric[] = ['Reachability', 'Latency', 'PacketLoss', 'Cpu', 'Memory', 'InterfaceUtilization'];
const SEVERITIES: AlertSeverity[] = ['Info', 'Warning', 'Major', 'Critical'];
const TARGETS: RuleTargetKind[] = ['All', 'Device', 'DeviceType', 'Tag', 'Site'];
const CHANNELS = ['inapp', 'email', 'webhook'];

const BLANK: SaveRulePayload = {
  name: '',
  description: '',
  metric: 'Latency',
  comparison: 'GreaterThan',
  threshold: 100,
  durationSeconds: 120,
  severity: 'Warning',
  targetKind: 'All',
  targetValue: '',
  enabled: true,
  notifyChannels: ['inapp']
};

export function RulesPage() {
  const { isAdmin } = useAuth();
  const { push } = useToasts();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<AlertRule | 'new' | null>(null);

  const { data: rules, isLoading } = useQuery({ queryKey: ['rules'], queryFn: () => api.rules() });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['rules'] });

  const save = useMutation({
    mutationFn: ({ id, payload }: { id?: string; payload: SaveRulePayload }) =>
      id ? api.updateRule(id, payload) : api.createRule(payload),
    onSuccess: (rule) => {
      push({ title: `Rule "${rule.name}" saved`, tone: 'good' });
      setEditing(null);
      invalidate();
    }
  });

  const toggle = useMutation({
    mutationFn: (id: string) => api.toggleRule(id),
    onSuccess: invalidate
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteRule(id),
    onSuccess: () => {
      push({ title: 'Rule deleted', tone: 'info' });
      invalidate();
    }
  });

  return (
    <div className="page">
      <div className="toolbar">
        <span className="muted">
          Rules are evaluated on every poll. A rule fires once its metric stays out of bounds for the whole duration.
        </span>
        <span className="spacer" />
        {isAdmin && (
          <button className="btn primary" onClick={() => setEditing('new')}>
            New rule
          </button>
        )}
      </div>

      <section className="card" style={{ padding: 0 }}>
        {isLoading ? (
          <div style={{ padding: 16 }}>
            <Skeleton height={220} />
          </div>
        ) : !rules?.length ? (
          <Empty title="No rules yet" hint="Add a rule to start raising alerts." />
        ) : (
          <div style={{ padding: '14px 4px 4px' }}>
            <table>
              <thead>
                <tr>
                  <th>Rule</th>
                  <th>Condition</th>
                  <th>Applies to</th>
                  <th>Severity</th>
                  <th>Notifies</th>
                  <th>State</th>
                  <th className="num">Updated</th>
                  {isAdmin && <th />}
                </tr>
              </thead>
              <tbody>
                {rules.map((rule) => (
                  <tr key={rule.id}>
                    <td>
                      <div style={{ fontWeight: 500 }}>{rule.name}</div>
                      {rule.description && <span className="muted" style={{ fontSize: 12 }}>{rule.description}</span>}
                    </td>
                    <td>
                      {metricLabel(rule.metric)} {rule.comparison === 'GreaterThan' ? '>' : '<'} {rule.threshold}
                      {metricUnit(rule.metric)}
                      {rule.durationSeconds > 0 && (
                        <span className="muted"> for {rule.durationSeconds}s</span>
                      )}
                    </td>
                    <td>
                      {rule.targetKind === 'All' ? 'All devices' : `${rule.targetKind}: ${rule.targetValue}`}
                    </td>
                    <td>
                      <SeverityBadge severity={rule.severity} />
                    </td>
                    <td className="muted">{rule.notifyChannels.join(', ')}</td>
                    <td>
                      <button
                        className="btn ghost sm"
                        disabled={!isAdmin}
                        onClick={() => toggle.mutate(rule.id)}
                      >
                        {rule.enabled ? 'Enabled' : 'Disabled'}
                      </button>
                    </td>
                    <td className="num muted">{stamp(rule.updatedAt)}</td>
                    {isAdmin && (
                      <td>
                        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                          <button className="btn ghost sm" onClick={() => setEditing(rule)}>
                            Edit
                          </button>
                          <button
                            className="btn ghost sm"
                            onClick={() => {
                              if (window.confirm(`Delete "${rule.name}"?`)) {
                                remove.mutate(rule.id);
                              }
                            }}
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {editing && (
        <RuleForm
          rule={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSave={(payload) => save.mutate({ id: editing === 'new' ? undefined : editing.id, payload })}
          busy={save.isPending}
        />
      )}
    </div>
  );
}

function RuleForm({
  rule,
  onClose,
  onSave,
  busy
}: {
  rule: AlertRule | null;
  onClose: () => void;
  onSave: (payload: SaveRulePayload) => void;
  busy: boolean;
}) {
  const [form, setForm] = useState<SaveRulePayload>(
    rule
      ? {
          name: rule.name,
          description: rule.description ?? '',
          metric: rule.metric,
          comparison: rule.comparison,
          threshold: rule.threshold,
          durationSeconds: rule.durationSeconds,
          severity: rule.severity,
          targetKind: rule.targetKind,
          targetValue: rule.targetValue ?? '',
          enabled: rule.enabled,
          notifyChannels: rule.notifyChannels
        }
      : BLANK
  );

  const set = <K extends keyof SaveRulePayload>(key: K, value: SaveRulePayload[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const toggleChannel = (channel: string) =>
    set(
      'notifyChannels',
      form.notifyChannels.includes(channel)
        ? form.notifyChannels.filter((value) => value !== channel)
        : [...form.notifyChannels, channel]
    );

  return (
    <Modal
      title={rule ? `Edit ${rule.name}` : 'New alert rule'}
      subtitle="Thresholds are checked against every poll of every matching device"
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" disabled={busy || !form.name.trim()} onClick={() => onSave(form)}>
            {busy ? 'Saving…' : 'Save rule'}
          </button>
        </>
      }
    >
      <div className="field">
        <label>Name</label>
        <input value={form.name} onChange={(event) => set('name', event.target.value)} placeholder="Uplink congestion" />
      </div>

      <div className="field">
        <label>Description</label>
        <input
          value={form.description ?? ''}
          onChange={(event) => set('description', event.target.value)}
          placeholder="What does this mean for the operator?"
        />
      </div>

      <div className="grid" style={{ gridTemplateColumns: '1fr 1fr 1fr' }}>
        <div className="field">
          <label>Metric</label>
          <select value={form.metric} onChange={(event) => set('metric', event.target.value as AlertMetric)}>
            {METRICS.map((metric) => (
              <option key={metric} value={metric}>
                {metricLabel(metric)}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label>Comparison</label>
          <select value={form.comparison} onChange={(event) => set('comparison', event.target.value as Comparison)}>
            <option value="GreaterThan">Above</option>
            <option value="LessThan">Below</option>
          </select>
        </div>
        <div className="field">
          <label>Threshold ({metricUnit(form.metric) || 'flag'})</label>
          <input type="number" value={form.threshold} onChange={(event) => set('threshold', Number(event.target.value))} />
        </div>
      </div>

      <div className="grid" style={{ gridTemplateColumns: '1fr 1fr 1fr' }}>
        <div className="field">
          <label>Sustained for (seconds)</label>
          <input
            type="number"
            min={0}
            max={86400}
            value={form.durationSeconds}
            onChange={(event) => set('durationSeconds', Number(event.target.value))}
          />
        </div>
        <div className="field">
          <label>Severity</label>
          <select value={form.severity} onChange={(event) => set('severity', event.target.value as AlertSeverity)}>
            {SEVERITIES.map((severity) => (
              <option key={severity} value={severity}>
                {severity}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label>Applies to</label>
          <select value={form.targetKind} onChange={(event) => set('targetKind', event.target.value as RuleTargetKind)}>
            {TARGETS.map((target) => (
              <option key={target} value={target}>
                {target === 'All' ? 'All devices' : target}
              </option>
            ))}
          </select>
        </div>
      </div>

      {form.targetKind !== 'All' && (
        <div className="field">
          <label>Target value</label>
          <input
            value={form.targetValue ?? ''}
            onChange={(event) => set('targetValue', event.target.value)}
            placeholder={form.targetKind === 'Tag' ? 'critical' : form.targetKind === 'DeviceType' ? 'Switch' : 'Main Campus'}
          />
        </div>
      )}

      <div className="field">
        <label>Notification channels</label>
        <div style={{ display: 'flex', gap: 14 }}>
          {CHANNELS.map((channel) => (
            <label key={channel} style={{ display: 'flex', gap: 6, alignItems: 'center', fontSize: 13 }}>
              <input
                type="checkbox"
                checked={form.notifyChannels.includes(channel)}
                onChange={() => toggleChannel(channel)}
              />
              {channel}
            </label>
          ))}
        </div>
      </div>

      <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 13 }}>
        <input type="checkbox" checked={form.enabled} onChange={(event) => set('enabled', event.target.checked)} />
        Rule is armed
      </label>
    </Modal>
  );
}
