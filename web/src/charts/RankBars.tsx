interface Row {
  label: string;
  sublabel?: string;
  value: number;
  display: string;
  tone?: 'normal' | 'warn' | 'hot';
}

export function RankBars({ rows, max }: { rows: Row[]; max?: number }) {
  if (!rows.length) {
    return <div className="empty">Nothing to rank yet.</div>;
  }

  const ceiling = max ?? Math.max(...rows.map((row) => row.value), 1);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {rows.map((row) => (
        <div key={row.label + (row.sublabel ?? '')} style={{ display: 'grid', gap: 4 }}>
          <div style={{ display: 'flex', gap: 10, alignItems: 'baseline' }}>
            <span style={{ fontWeight: 500 }}>{row.label}</span>
            {row.sublabel && <span className="mono">{row.sublabel}</span>}
            <span className="num" style={{ marginLeft: 'auto', fontVariantNumeric: 'tabular-nums' }}>
              {row.display}
            </span>
          </div>
          <div className="meter">
            <span
              className={row.tone === 'hot' ? 'hot' : row.tone === 'warn' ? 'warn' : undefined}
              style={{ width: `${Math.min(100, (row.value / ceiling) * 100)}%` }}
            />
          </div>
        </div>
      ))}
    </div>
  );
}
