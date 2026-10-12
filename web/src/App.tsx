import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth/AuthProvider';
import { LoginPage } from './auth/LoginPage';
import { AppShell } from './components/AppShell';
import { LiveProvider } from './realtime/LiveProvider';
import { DashboardPage } from './features/dashboard/DashboardPage';
import { DevicesPage } from './features/devices/DevicesPage';
import { DeviceDetailPage } from './features/devices/DeviceDetailPage';
import { TopologyPage } from './features/topology/TopologyPage';
import { AlertsPage } from './features/alerts/AlertsPage';
import { RulesPage } from './features/alerts/RulesPage';
import { ReportsPage } from './features/reports/ReportsPage';

export function App() {
  const { user, ready } = useAuth();

  if (!ready) {
    return <div className="login">Loading…</div>;
  }

  if (!user) {
    return <LoginPage />;
  }

  return (
    <LiveProvider>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/devices" element={<DevicesPage />} />
          <Route path="/devices/:id" element={<DeviceDetailPage />} />
          <Route path="/topology" element={<TopologyPage />} />
          <Route path="/alerts" element={<AlertsPage />} />
          <Route path="/rules" element={<RulesPage />} />
          <Route path="/reports" element={<ReportsPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </LiveProvider>
  );
}
