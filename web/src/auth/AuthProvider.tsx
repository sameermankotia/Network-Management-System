import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api, getToken, setToken } from '@/lib/api';

interface Session {
  id: string;
  email: string;
  displayName: string;
  role: string;
}

interface AuthValue {
  user: Session | null;
  ready: boolean;
  isAdmin: boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    if (!getToken()) {
      setReady(true);
      return;
    }

    api
      .me()
      .then(setUser)
      .catch(() => setToken(null))
      .finally(() => setReady(true));
  }, []);

  useEffect(() => {
    const onUnauthorized = () => setUser(null);
    window.addEventListener('nms:unauthorized', onUnauthorized);
    return () => window.removeEventListener('nms:unauthorized', onUnauthorized);
  }, []);

  const signIn = useCallback(async (email: string, password: string) => {
    const result = await api.login(email, password);
    setToken(result.token);
    setUser({ id: result.id, email: result.email, displayName: result.displayName, role: result.role });
  }, []);

  const signOut = useCallback(() => {
    setToken(null);
    setUser(null);
  }, []);

  const value = useMemo<AuthValue>(
    () => ({ user, ready, isAdmin: user?.role === 'admin', signIn, signOut }),
    [user, ready, signIn, signOut]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used inside AuthProvider');
  }
  return context;
}
