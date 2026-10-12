import type {
  Alert,
  AlertDetail,
  AlertReport,
  AlertRule,
  AuthenticatedUser,
  AvailabilityReport,
  ConnectivityPath,
  CycleSummary,
  DashboardOverview,
  Device,
  DeviceDetail,
  DiagnosticKind,
  DiagnosticRun,
  NamedSeries,
  SaveDevicePayload,
  SaveRulePayload,
  TopologyGraph,
  UtilizationReport
} from './types';

const TOKEN_KEY = 'nms.token';

let token: string | null = readStoredToken();

function readStoredToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

export function setToken(next: string | null) {
  token = next;
  try {
    if (next) {
      localStorage.setItem(TOKEN_KEY, next);
    } else {
      localStorage.removeItem(TOKEN_KEY);
    }
  } catch {
    // private browsing: the in-memory token still works for this session
  }
}

export function getToken() {
  return token;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly errors?: Record<string, string[]>
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (init?.body) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(path, { ...init, headers });

  if (response.status === 401) {
    setToken(null);
    window.dispatchEvent(new CustomEvent('nms:unauthorized'));
    throw new ApiError(401, 'Session expired. Please sign in again.');
  }

  if (!response.ok) {
    throw new ApiError(response.status, await describe(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const type = response.headers.get('content-type') ?? '';
  return type.includes('json') ? ((await response.json()) as T) : ((await response.text()) as unknown as T);
}

async function describe(response: Response): Promise<string> {
  try {
    const body = await response.json();
    if (body.errors) {
      const first = Object.values(body.errors as Record<string, string[]>)[0];
      if (first?.length) {
        return first[0];
      }
    }
    return body.detail ?? body.title ?? body.message ?? response.statusText;
  } catch {
    return response.statusText || `Request failed with ${response.status}`;
  }
}

const query = (params: Record<string, string | number | boolean | null | undefined>) => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text ? `?${text}` : '';
};

export type DeviceFilters = Partial<Record<'search' | 'status' | 'type' | 'site' | 'tag', string>>;

export const api = {
  login: (email: string, password: string) =>
    request<AuthenticatedUser>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),

  me: () => request<{ id: string; email: string; displayName: string; role: string }>('/api/auth/me'),

  dashboard: (hours: number) => request<DashboardOverview>(`/api/dashboard${query({ hours })}`),

  monitorStatus: () => request<{ lastCycle: CycleSummary | null }>('/api/monitor/status'),

  devices: (filters: DeviceFilters = {}) => request<Device[]>(`/api/devices${query(filters)}`),

  device: (id: string) => request<DeviceDetail>(`/api/devices/${id}`),

  deviceSeries: (id: string, hours: number, bucketSeconds?: number) =>
    request<NamedSeries[]>(`/api/devices/${id}/series${query({ hours, bucketSeconds })}`),

  deviceAlerts: (id: string) => request<Alert[]>(`/api/devices/${id}/alerts`),

  createDevice: (payload: SaveDevicePayload) =>
    request<Device>('/api/devices', { method: 'POST', body: JSON.stringify(payload) }),

  updateDevice: (id: string, payload: SaveDevicePayload) =>
    request<Device>(`/api/devices/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  deleteDevice: (id: string) => request<void>(`/api/devices/${id}`, { method: 'DELETE' }),

  setMaintenance: (id: string, inMaintenance: boolean) =>
    request<{ id: string; status: string }>(`/api/devices/${id}/maintenance`, {
      method: 'POST',
      body: JSON.stringify({ inMaintenance })
    }),

  diagnostics: (id: string) => request<DiagnosticRun[]>(`/api/devices/${id}/diagnostics`),

  runDiagnostic: (id: string, kind: DiagnosticKind, port?: number) =>
    request<DiagnosticRun>(`/api/devices/${id}/diagnostics`, {
      method: 'POST',
      body: JSON.stringify({ kind, port })
    }),

  topology: (site?: string) => request<TopologyGraph>(`/api/topology${query({ site })}`),

  path: (from: string, to: string) => request<ConnectivityPath>(`/api/topology/path${query({ from, to })}`),

  alerts: (params: { state?: string; severity?: string; deviceId?: string; hours?: number; limit?: number } = {}) =>
    request<Alert[]>(`/api/alerts${query(params)}`),

  alert: (id: string) => request<AlertDetail>(`/api/alerts/${id}`),

  acknowledge: (id: string, note?: string) =>
    request<Alert>(`/api/alerts/${id}/acknowledge`, { method: 'POST', body: JSON.stringify({ note }) }),

  resolve: (id: string, note?: string) =>
    request<Alert>(`/api/alerts/${id}/resolve`, { method: 'POST', body: JSON.stringify({ note }) }),

  comment: (id: string, message: string) =>
    request<void>(`/api/alerts/${id}/comments`, { method: 'POST', body: JSON.stringify({ message }) }),

  acknowledgeMany: (ids: string[]) =>
    request<{ acknowledged: number }>('/api/alerts/acknowledge', { method: 'POST', body: JSON.stringify(ids) }),

  rules: () => request<AlertRule[]>('/api/alert-rules'),

  createRule: (payload: SaveRulePayload) =>
    request<AlertRule>('/api/alert-rules', { method: 'POST', body: JSON.stringify(payload) }),

  updateRule: (id: string, payload: SaveRulePayload) =>
    request<AlertRule>(`/api/alert-rules/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  toggleRule: (id: string) => request<{ id: string; enabled: boolean }>(`/api/alert-rules/${id}/toggle`, { method: 'POST' }),

  deleteRule: (id: string) => request<void>(`/api/alert-rules/${id}`, { method: 'DELETE' }),

  utilizationReport: (hours: number, deviceId?: string) =>
    request<UtilizationReport>(`/api/reports/utilization${query({ hours, deviceId })}`),

  availabilityReport: (days: number) => request<AvailabilityReport>(`/api/reports/availability${query({ days })}`),

  alertReport: (days: number, granularity: string) =>
    request<AlertReport>(`/api/reports/alerts${query({ days, granularity })}`),

  exportUrl: (report: 'utilization' | 'availability', params: Record<string, string | number>) =>
    `/api/reports/${report}${query({ ...params, format: 'csv' })}`
};
