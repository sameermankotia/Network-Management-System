import { useState } from 'react';
import { useAuth } from './AuthProvider';
import { ApiError } from '@/lib/api';

export function LoginPage() {
  const { signIn } = useAuth();
  const [email, setEmail] = useState('admin@campus.edu');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await signIn(email.trim(), password);
    } catch (problem) {
      setError(problem instanceof ApiError ? problem.message : 'Could not reach the API.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login">
      <form className="login-card" onSubmit={submit}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span className="brand-mark" style={{ width: 34, height: 34 }}>
            NMS
          </span>
          <div>
            <h1>Campus Network Operations</h1>
            <p>Sign in to monitor campus infrastructure</p>
          </div>
        </div>

        <div className="field">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </div>

        <div className="field">
          <label htmlFor="password">Password</label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </div>

        {error && <div className="field error">{error}</div>}

        <button className="btn primary" type="submit" disabled={busy || !password}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  );
}
