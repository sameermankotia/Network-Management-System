import { useState } from 'react';
import type { DeviceDetail, DeviceType, SaveDevicePayload } from '@/lib/types';
import { Modal } from '@/components/Bits';
import { deviceTypeLabel } from '@/lib/format';

const TYPES: DeviceType[] = ['Router', 'Switch', 'Firewall', 'AccessPoint', 'Server', 'Ups', 'Printer'];

interface Props {
  existing?: DeviceDetail;
  onClose: () => void;
  onSave: (payload: SaveDevicePayload) => Promise<void>;
}

export function DeviceForm({ existing, onClose, onSave }: Props) {
  const device = existing?.device;
  const [form, setForm] = useState<SaveDevicePayload>({
    name: device?.name ?? '',
    hostname: device?.hostname ?? '',
    ipAddress: device?.ipAddress ?? '',
    deviceType: device?.deviceType ?? 'Switch',
    vendor: device?.vendor ?? '',
    model: device?.model ?? '',
    osVersion: existing?.osVersion ?? '',
    site: device?.site ?? 'Main Campus',
    building: device?.building ?? '',
    room: device?.room ?? '',
    managementUrl: existing?.managementUrl ?? '',
    pollIntervalSeconds: device?.pollIntervalSeconds ?? 60,
    isMonitored: device?.isMonitored ?? true,
    tags: device?.tags ?? [],
    notes: existing?.notes ?? ''
  });
  const [tagText, setTagText] = useState((device?.tags ?? []).join(', '));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const set = <K extends keyof SaveDevicePayload>(key: K, value: SaveDevicePayload[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const submit = async () => {
    setBusy(true);
    setError(null);

    try {
      await onSave({
        ...form,
        tags: tagText
          .split(',')
          .map((tag) => tag.trim())
          .filter(Boolean)
      });
      onClose();
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Could not save the device.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={device ? `Edit ${device.name}` : 'Add device'}
      subtitle="Inventory details drive polling, topology and rule targeting"
      onClose={onClose}
      footer={
        <>
          {error && <span className="field error" style={{ marginRight: 'auto' }}>{error}</span>}
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={submit} disabled={busy || !form.name || !form.hostname || !form.ipAddress}>
            {busy ? 'Saving…' : device ? 'Save changes' : 'Add device'}
          </button>
        </>
      }
    >
      <div className="grid" style={{ gridTemplateColumns: '1fr 1fr' }}>
        <div className="field">
          <label>Display name</label>
          <input value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="Engineering Dist" />
        </div>
        <div className="field">
          <label>Hostname</label>
          <input value={form.hostname} onChange={(e) => set('hostname', e.target.value)} placeholder="eng-dist-01.campus.edu" />
        </div>
        <div className="field">
          <label>Management IP</label>
          <input value={form.ipAddress} onChange={(e) => set('ipAddress', e.target.value)} placeholder="10.10.1.1" />
        </div>
        <div className="field">
          <label>Type</label>
          <select value={form.deviceType} onChange={(e) => set('deviceType', e.target.value as DeviceType)}>
            {TYPES.map((type) => (
              <option key={type} value={type}>
                {deviceTypeLabel(type)}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label>Vendor</label>
          <input value={form.vendor ?? ''} onChange={(e) => set('vendor', e.target.value)} />
        </div>
        <div className="field">
          <label>Model</label>
          <input value={form.model ?? ''} onChange={(e) => set('model', e.target.value)} />
        </div>
        <div className="field">
          <label>OS version</label>
          <input value={form.osVersion ?? ''} onChange={(e) => set('osVersion', e.target.value)} />
        </div>
        <div className="field">
          <label>Site</label>
          <input value={form.site} onChange={(e) => set('site', e.target.value)} />
        </div>
        <div className="field">
          <label>Building</label>
          <input value={form.building ?? ''} onChange={(e) => set('building', e.target.value)} />
        </div>
        <div className="field">
          <label>Room</label>
          <input value={form.room ?? ''} onChange={(e) => set('room', e.target.value)} />
        </div>
        <div className="field">
          <label>Poll interval (seconds)</label>
          <input
            type="number"
            min={10}
            max={3600}
            value={form.pollIntervalSeconds}
            onChange={(e) => set('pollIntervalSeconds', Number(e.target.value))}
          />
        </div>
        <div className="field">
          <label>Management URL</label>
          <input value={form.managementUrl ?? ''} onChange={(e) => set('managementUrl', e.target.value)} placeholder="https://" />
        </div>
      </div>

      <div className="field">
        <label>Tags (comma separated)</label>
        <input value={tagText} onChange={(e) => setTagText(e.target.value)} placeholder="core, critical" />
      </div>

      <div className="field">
        <label>Notes</label>
        <textarea rows={3} value={form.notes ?? ''} onChange={(e) => set('notes', e.target.value)} />
      </div>

      <label style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 13 }}>
        <input type="checkbox" checked={form.isMonitored} onChange={(e) => set('isMonitored', e.target.checked)} />
        Poll this device
      </label>
    </Modal>
  );
}
