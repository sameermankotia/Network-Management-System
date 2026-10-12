import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { useAuth } from '@/auth/AuthProvider';
import { useToasts } from '@/components/Toasts';
import { getToken } from '@/lib/api';
import type { Alert, CycleSummary, LinkChange, LiveSample, StatusChange } from '@/lib/types';

type SampleHandler = (sample: LiveSample) => void;

interface LiveValue {
  connected: boolean;
  lastCycle: CycleSummary | null;
  watch: (deviceId: string, handler: SampleHandler) => () => void;
}

const LiveContext = createContext<LiveValue | null>(null);

export function LiveProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const { push } = useToasts();
  const queryClient = useQueryClient();

  const [connected, setConnected] = useState(false);
  const [lastCycle, setLastCycle] = useState<CycleSummary | null>(null);

  const connectionRef = useRef<HubConnection | null>(null);
  const handlersRef = useRef(new Map<string, Set<SampleHandler>>());
  const lastDashboardRefresh = useRef(0);

  useEffect(() => {
    if (!user) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/monitoring', { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('SampleRecorded', (sample: LiveSample) => {
      handlersRef.current.get(sample.deviceId)?.forEach((handler) => handler(sample));
    });

    connection.on('DeviceStatusChanged', (change: StatusChange) => {
      queryClient.invalidateQueries({ queryKey: ['devices'] });
      queryClient.invalidateQueries({ queryKey: ['device', change.deviceId] });
      queryClient.invalidateQueries({ queryKey: ['topology'] });

      if (change.status === 'Down') {
        push({ title: `${change.name} went down`, body: 'No response to the last two polls.', tone: 'critical' });
      } else if (change.previous === 'Down' && change.status === 'Up') {
        push({ title: `${change.name} recovered`, body: 'Responding again.', tone: 'good' });
      }
    });

    connection.on('AlertRaised', (alert: Alert) => {
      queryClient.invalidateQueries({ queryKey: ['alerts'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      push({
        title: alert.title,
        body: alert.ruleName ? `${alert.ruleName} · ${alert.deviceName}` : alert.deviceName,
        tone: alert.severity.toLowerCase() as 'critical' | 'major' | 'warning' | 'info'
      });
    });

    connection.on('AlertChanged', () => {
      queryClient.invalidateQueries({ queryKey: ['alerts'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    });

    connection.on('LinkStatusChanged', (_: LinkChange) => {
      queryClient.invalidateQueries({ queryKey: ['topology'] });
    });

    connection.on('CycleCompleted', (cycle: CycleSummary) => {
      setLastCycle(cycle);

      // Cycle events arrive every few seconds; refresh the heavier aggregates at
      // a human pace instead of on every tick.
      const now = Date.now();
      if (now - lastDashboardRefresh.current > 20000) {
        lastDashboardRefresh.current = now;
        queryClient.invalidateQueries({ queryKey: ['dashboard'] });
        queryClient.invalidateQueries({ queryKey: ['devices'] });
      }
    });

    connection.onreconnected(() => setConnected(true));
    connection.onreconnecting(() => setConnected(false));
    connection.onclose(() => setConnected(false));

    connection
      .start()
      .then(() => {
        setConnected(true);
        connectionRef.current = connection;
        for (const deviceId of handlersRef.current.keys()) {
          void connection.invoke('WatchDevice', deviceId);
        }
      })
      .catch(() => setConnected(false));

    connectionRef.current = connection;

    return () => {
      connectionRef.current = null;
      setConnected(false);
      void connection.stop();
    };
  }, [user, push, queryClient]);

  const value = useMemo<LiveValue>(
    () => ({
      connected,
      lastCycle,
      watch: (deviceId, handler) => {
        const handlers = handlersRef.current;
        const existing = handlers.get(deviceId);

        if (existing) {
          existing.add(handler);
        } else {
          handlers.set(deviceId, new Set([handler]));
          const connection = connectionRef.current;
          if (connection?.state === HubConnectionState.Connected) {
            void connection.invoke('WatchDevice', deviceId);
          }
        }

        return () => {
          const set = handlers.get(deviceId);
          if (!set) return;
          set.delete(handler);
          if (set.size === 0) {
            handlers.delete(deviceId);
            const connection = connectionRef.current;
            if (connection?.state === HubConnectionState.Connected) {
              void connection.invoke('UnwatchDevice', deviceId);
            }
          }
        };
      }
    }),
    [connected, lastCycle]
  );

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>;
}

export function useLive() {
  const context = useContext(LiveContext);
  if (!context) {
    throw new Error('useLive must be used inside LiveProvider');
  }
  return context;
}

// Streams samples for one device into a bounded buffer the caller can chart.
export function useDeviceStream(deviceId: string, capacity = 120) {
  const { watch } = useLive();
  const [samples, setSamples] = useState<LiveSample[]>([]);

  useEffect(() => {
    setSamples([]);
    return watch(deviceId, (sample) => {
      setSamples((current) => [...current, sample].slice(-capacity));
    });
  }, [deviceId, capacity, watch]);

  return samples;
}
