import { createContext, useCallback, useContext, useMemo, useState } from 'react';

type Tone = 'info' | 'good' | 'warning' | 'major' | 'critical';

interface Toast {
  id: number;
  title: string;
  body?: string;
  tone: Tone;
}

interface ToastValue {
  push: (toast: { title: string; body?: string; tone?: Tone }) => void;
}

const ToastContext = createContext<ToastValue | null>(null);
let nextId = 1;

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const push = useCallback(({ title, body, tone = 'info' }: { title: string; body?: string; tone?: Tone }) => {
    const id = nextId++;
    setToasts((current) => [...current.slice(-4), { id, title, body, tone }]);
    window.setTimeout(() => setToasts((current) => current.filter((t) => t.id !== id)), 7000);
  }, []);

  const value = useMemo(() => ({ push }), [push]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toasts" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast ${toast.tone}`} onClick={() => setToasts((c) => c.filter((t) => t.id !== toast.id))}>
            <strong>{toast.title}</strong>
            {toast.body && <span>{toast.body}</span>}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToasts() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToasts must be used inside ToastProvider');
  }
  return context;
}
