import type { AlertSeverity, DeviceStatus, LinkStatus } from '@/lib/types';

export function StatusPill({ status, label, pulse }: { status: DeviceStatus | LinkStatus; label?: string; pulse?: boolean }) {
  return (
    <span className={`status ${status.toLowerCase()}`}>
      <span className={`dot ${pulse && status === 'Down' ? 'pulse' : ''}`} />
      {label ?? status}
    </span>
  );
}

export function SeverityBadge({ severity }: { severity: AlertSeverity }) {
  const glyph = severity === 'Critical' ? '●' : severity === 'Major' ? '◆' : severity === 'Warning' ? '▲' : '○';
  return (
    <span className={`severity ${severity.toLowerCase()}`}>
      <span aria-hidden="true">{glyph}</span>
      {severity}
    </span>
  );
}

export function Stat({
  label,
  value,
  unit,
  foot,
  children
}: {
  label: string;
  value: string | number;
  unit?: string;
  foot?: React.ReactNode;
  children?: React.ReactNode;
}) {
  return (
    <div className="stat">
      <span className="label">{label}</span>
      <span className="value">
        {value}
        {unit && <small>{unit}</small>}
      </span>
      {children}
      {foot && <span className="foot">{foot}</span>}
    </div>
  );
}

export function Meter({
  value,
  warnAt = 70,
  hotAt = 85,
  invert = false
}: {
  value?: number | null;
  warnAt?: number;
  hotAt?: number;
  invert?: boolean;
}) {
  if (value === null || value === undefined) {
    return <span className="muted">—</span>;
  }

  const tone = invert
    ? value <= hotAt
      ? 'hot'
      : value <= warnAt
        ? 'warn'
        : undefined
    : value >= hotAt
      ? 'hot'
      : value >= warnAt
        ? 'warn'
        : undefined;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <div className="meter">
        <span className={tone} style={{ width: `${Math.min(100, Math.max(2, value))}%` }} />
      </div>
      <span style={{ fontVariantNumeric: 'tabular-nums', minWidth: 42, textAlign: 'right' }}>{value.toFixed(1)}%</span>
    </div>
  );
}

export function Empty({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="empty">
      <strong>{title}</strong>
      {hint && <span>{hint}</span>}
    </div>
  );
}

export function Skeleton({ height = 120 }: { height?: number }) {
  return <div className="skeleton" style={{ height }} />;
}

export function SearchIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <circle cx="11" cy="11" r="7" />
      <path d="M20 20l-4.2-4.2" />
    </svg>
  );
}

export function Modal({
  title,
  subtitle,
  onClose,
  children,
  footer
}: {
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  return (
    <div className="scrim center" onClick={onClose}>
      <div className="modal" onClick={(event) => event.stopPropagation()}>
        <div className="panel-head">
          <div>
            <h2>{title}</h2>
            {subtitle && <span className="muted">{subtitle}</span>}
          </div>
          <button className="btn ghost sm" style={{ marginLeft: 'auto' }} onClick={onClose}>
            Close
          </button>
        </div>
        <div className="panel-body">{children}</div>
        {footer && <div className="panel-foot">{footer}</div>}
      </div>
    </div>
  );
}

export function Drawer({
  title,
  subtitle,
  onClose,
  children,
  footer
}: {
  title: string;
  subtitle?: React.ReactNode;
  onClose: () => void;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  return (
    <div className="scrim right" onClick={onClose}>
      <aside className="drawer" onClick={(event) => event.stopPropagation()}>
        <div className="panel-head">
          <div style={{ minWidth: 0 }}>
            <h2>{title}</h2>
            {subtitle && <div className="muted" style={{ fontSize: 12 }}>{subtitle}</div>}
          </div>
          <button className="btn ghost sm" style={{ marginLeft: 'auto' }} onClick={onClose}>
            Close
          </button>
        </div>
        <div className="panel-body">{children}</div>
        {footer && <div className="panel-foot">{footer}</div>}
      </aside>
    </div>
  );
}

export function Segmented<T extends string | number>({
  options,
  value,
  onChange
}: {
  options: { label: string; value: T }[];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div className="segmented">
      {options.map((option) => (
        <button
          key={String(option.value)}
          className={option.value === value ? 'active' : ''}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
