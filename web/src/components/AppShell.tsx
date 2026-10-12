import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useAuth } from '@/auth/AuthProvider';
import { useLive } from '@/realtime/LiveProvider';
import { api } from '@/lib/api';
import { ago, mbps } from '@/lib/format';

const PAGES = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/devices', label: 'Devices' },
  { to: '/topology', label: 'Topology' },
  { to: '/alerts', label: 'Alerts' },
  { to: '/rules', label: 'Alert rules' },
  { to: '/reports', label: 'Reports' }
];

const TITLES: Record<string, { title: string; subtitle: string }> = {
  '/': { title: 'Operations dashboard', subtitle: 'Fleet health, traffic and open incidents' },
  '/devices': { title: 'Devices', subtitle: 'Inventory, live state and diagnostics' },
  '/topology': { title: 'Topology', subtitle: 'How the campus network is wired together' },
  '/alerts': { title: 'Alerts', subtitle: 'Triage queue with acknowledgment workflow' },
  '/rules': { title: 'Alert rules', subtitle: 'Thresholds, severities and notification channels' },
  '/reports': { title: 'Reports', subtitle: 'Utilization, uptime and alert analytics' }
};

function useTheme() {
  const [theme, setTheme] = useState<'dark' | 'light'>(() => {
    try {
      return (localStorage.getItem('nms.theme') as 'dark' | 'light') ?? 'dark';
    } catch {
      return 'dark';
    }
  });

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem('nms.theme', theme);
    } catch {
      // not fatal, the theme just won't stick
    }
  }, [theme]);

  return [theme, () => setTheme(theme === 'dark' ? 'light' : 'dark')] as const;
}

export function AppShell() {
  const { user, signOut } = useAuth();
  const { connected, lastCycle } = useLive();
  const location = useLocation();
  const [theme, toggleTheme] = useTheme();

  const { data: alerts } = useQuery({
    queryKey: ['alerts', 'badge'],
    queryFn: () => api.alerts({ limit: 200 }),
    refetchInterval: 30000
  });

  const openAlerts = alerts?.length ?? 0;
  const critical = alerts?.filter((alert) => alert.severity === 'Critical').length ?? 0;
  const page = TITLES[location.pathname] ?? { title: 'Campus NMS', subtitle: '' };

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">NMS</span>
          <span className="brand-text">
            <strong>Campus Network</strong>
            <span>Operations</span>
          </span>
        </div>

        <nav className="nav">
          <span className="nav-label">Monitor</span>
          {PAGES.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
            >
              {item.label}
              {item.to === '/alerts' && openAlerts > 0 && (
                <span className={`count ${critical > 0 ? 'alarm' : ''}`}>{openAlerts}</span>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="status up" style={{ gap: 7 }}>
            <span className="dot" style={{ background: connected ? 'var(--good)' : 'var(--ink-muted)' }} />
            {connected ? 'Live feed connected' : 'Reconnecting…'}
          </div>
          {lastCycle && (
            <span>
              {lastCycle.devicesPolled} polled · {mbps(lastCycle.totalInMbps + lastCycle.totalOutMbps)} · {ago(lastCycle.at)}
            </span>
          )}
          <button className="btn ghost sm" onClick={toggleTheme} style={{ justifyContent: 'flex-start' }}>
            {theme === 'dark' ? 'Light theme' : 'Dark theme'}
          </button>
        </div>
      </aside>

      <main className="main">
        <header className="topbar">
          <div>
            <h1>{page.title}</h1>
            <div className="subtitle">{page.subtitle}</div>
          </div>
          <div className="topbar-right">
            {lastCycle && (
              <span className="chip" title="Last completed poll cycle">
                cycle {lastCycle.durationMs} ms · {ago(lastCycle.at)}
              </span>
            )}
            <span className="chip">{user?.displayName}</span>
            <button className="btn sm" onClick={signOut}>
              Sign out
            </button>
          </div>
        </header>

        <div className="content">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
