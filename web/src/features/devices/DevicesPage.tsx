import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { ago, deviceTypeLabel, mbps, ms, pct, uptime } from '@/lib/format';
import { Empty, Meter, SearchIcon, Skeleton, StatusPill } from '@/components/Bits';
import { useAuth } from '@/auth/AuthProvider';
import { useToasts } from '@/components/Toasts';
import { DeviceForm } from './DeviceForm';
import type { DeviceStatus, DeviceType, SaveDevicePayload } from '@/lib/types';

const STATUSES: DeviceStatus[] = ['Up', 'Degraded', 'Down', 'Maintenance', 'Unknown'];
const TYPES: DeviceType[] = ['Router', 'Switch', 'Firewall', 'AccessPoint', 'Server', 'Ups', 'Printer'];

export function DevicesPage() {
  const navigate = useNavigate();
  const { isAdmin } = useAuth();
  const { push } = useToasts();
  const queryClient = useQueryClient();

  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [type, setType] = useState('');
  const [site, setSite] = useState('');
  const [adding, setAdding] = useState(false);

  const { data: devices, isLoading } = useQuery({
    queryKey: ['devices', { search, status, type, site }],
    queryFn: () => api.devices({ search, status, type, site }),
    refetchInterval: 20_000
  });

  const sites = useMemo(() => [...new Set((devices ?? []).map((device) => device.site))].sort(), [devices]);

  const create = useMutation({
    mutationFn: (payload: SaveDevicePayload) => api.createDevice(payload),
    onSuccess: (device) => {
      push({ title: `${device.name} added`, body: 'It will be polled on the next cycle.', tone: 'good' });
      queryClient.invalidateQueries({ queryKey: ['devices'] });
      queryClient.invalidateQueries({ queryKey: ['topology'] });
    }
  });

  const maintenance = useMutation({
    mutationFn: ({ id, on }: { id: string; on: boolean }) => api.setMaintenance(id, on),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['devices'] })
  });

  return (
    <div className="page">
      <div className="toolbar">
        <div className="search">
          <SearchIcon />
          <input placeholder="Search name, hostname or site" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>

        <select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Any state</option>
          {STATUSES.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>

        <select value={type} onChange={(e) => setType(e.target.value)}>
          <option value="">Any type</option>
          {TYPES.map((value) => (
            <option key={value} value={value}>
              {deviceTypeLabel(value)}
            </option>
          ))}
        </select>

        <select value={site} onChange={(e) => setSite(e.target.value)}>
          <option value="">Any site</option>
          {sites.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>

        <span className="spacer" />
        <span className="muted">{devices?.length ?? 0} devices</span>
        {isAdmin && (
          <button className="btn primary" onClick={() => setAdding(true)}>
            Add device
          </button>
        )}
      </div>

      <section className="card" style={{ padding: 0 }}>
        {isLoading ? (
          <div style={{ padding: 16 }}>
            <Skeleton height={260} />
          </div>
        ) : !devices?.length ? (
          <Empty title="No devices match those filters" hint="Clear the filters or add a device to start polling." />
        ) : (
          <div style={{ padding: '14px 4px 4px' }}>
            <table>
              <thead>
                <tr>
                  <th>Device</th>
                  <th>Type</th>
                  <th>Location</th>
                  <th>State</th>
                  <th className="num">Latency</th>
                  <th className="num">Loss</th>
                  <th style={{ width: 150 }}>CPU</th>
                  <th className="num">Traffic</th>
                  <th className="num">Uptime</th>
                  <th className="num">Alerts</th>
                  {isAdmin && <th />}
                </tr>
              </thead>
              <tbody>
                {devices.map((device) => (
                  <tr key={device.id} className="clickable" onClick={() => navigate(`/devices/${device.id}`)}>
                    <td>
                      <Link to={`/devices/${device.id}`} onClick={(event) => event.stopPropagation()}>
                        {device.name}
                      </Link>
                      <div className="mono">
                        {device.hostname} · {device.ipAddress}
                      </div>
                    </td>
                    <td>{deviceTypeLabel(device.deviceType)}</td>
                    <td>
                      {device.site}
                      {device.building && <div className="muted">{device.building} {device.room}</div>}
                    </td>
                    <td>
                      <StatusPill status={device.status} pulse />
                      <div className="muted" style={{ fontSize: 11 }}>{ago(device.lastSeenAt)}</div>
                    </td>
                    <td className="num">{ms(device.latencyMs)}</td>
                    <td className="num">{pct(device.packetLossPct)}</td>
                    <td>
                      <Meter value={device.cpuPct} />
                    </td>
                    <td className="num">{mbps((device.inMbps ?? 0) + (device.outMbps ?? 0))}</td>
                    <td className="num">{uptime(device.uptimeSeconds)}</td>
                    <td className="num">{device.openAlerts || '—'}</td>
                    {isAdmin && (
                      <td onClick={(event) => event.stopPropagation()}>
                        <button
                          className="btn ghost sm"
                          onClick={() =>
                            maintenance.mutate({ id: device.id, on: device.status !== 'Maintenance' })
                          }
                        >
                          {device.status === 'Maintenance' ? 'Resume' : 'Maintenance'}
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {adding && (
        <DeviceForm onClose={() => setAdding(false)} onSave={async (payload) => void (await create.mutateAsync(payload))} />
      )}
    </div>
  );
}
