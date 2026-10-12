import { useState } from 'react';
import { compact, linearScale, niceTicks, useElementWidth } from './primitives';

export interface StackRow {
  label: string;
  segments: { name: string; value: number; color: string }[];
}

interface Props {
  rows: StackRow[];
  height?: number;
  legend?: boolean;
}

const PADDING = { top: 10, right: 10, bottom: 22, left: 36 };
const GAP = 2;

export function StackedBars({ rows, height = 190, legend = true }: Props) {
  const [wrapRef, width] = useElementWidth<HTMLDivElement>();
  const [hover, setHover] = useState<number | null>(null);

  if (!rows.length) {
    return (
      <div className="chart" ref={wrapRef}>
        <div className="empty">Nothing recorded in this window.</div>
      </div>
    );
  }

  const plotWidth = Math.max(0, width - PADDING.left - PADDING.right);
  const plotHeight = height - PADDING.top - PADDING.bottom;
  const totals = rows.map((row) => row.segments.reduce((sum, segment) => sum + segment.value, 0));
  const ticks = niceTicks(0, Math.max(1, ...totals), 4);
  const y = linearScale([0, ticks[ticks.length - 1]], [PADDING.top + plotHeight, PADDING.top]);

  const slot = plotWidth / rows.length;
  const barWidth = Math.max(4, Math.min(28, slot - 6));
  const names = rows[0].segments.map((segment) => ({ name: segment.name, color: segment.color }));

  return (
    <div className="chart" ref={wrapRef}>
      {legend && (
        <div className="legend">
          {names.map((entry) => (
            <span className="legend-item" key={entry.name}>
              <span className="legend-swatch" style={{ background: entry.color }} />
              {entry.name}
            </span>
          ))}
        </div>
      )}

      <svg height={height} role="img" aria-label="Alerts by severity over time">
        <g className="axis">
          {ticks.map((tick) => (
            <g key={tick}>
              <line className="gridline" x1={PADDING.left} x2={PADDING.left + plotWidth} y1={y(tick)} y2={y(tick)} />
              <text x={PADDING.left - 8} y={y(tick) + 3} textAnchor="end">
                {compact(tick)}
              </text>
            </g>
          ))}
        </g>

        {rows.map((row, rowIndex) => {
          const centre = PADDING.left + slot * rowIndex + slot / 2;
          let cursor = PADDING.top + plotHeight;

          return (
            <g
              key={row.label}
              onMouseEnter={() => setHover(rowIndex)}
              onMouseLeave={() => setHover(null)}
            >
              <rect
                x={centre - slot / 2}
                y={PADDING.top}
                width={slot}
                height={plotHeight}
                fill={hover === rowIndex ? 'var(--surface-hover)' : 'transparent'}
              />
              {row.segments.map((segment) => {
                if (segment.value <= 0) return null;
                const segmentHeight = Math.max(2, PADDING.top + plotHeight - y(segment.value) - GAP);
                cursor -= segmentHeight + GAP;
                return (
                  <rect
                    key={segment.name}
                    x={centre - barWidth / 2}
                    y={cursor}
                    width={barWidth}
                    height={segmentHeight}
                    rx={3}
                    fill={segment.color}
                  />
                );
              })}
              {rowIndex % Math.ceil(rows.length / 6) === 0 && (
                <text className="axis" x={centre} y={height - 6} textAnchor="middle" fill="var(--ink-muted)" fontSize={10}>
                  {row.label}
                </text>
              )}
            </g>
          );
        })}

        <line className="baseline" x1={PADDING.left} x2={PADDING.left + plotWidth} y1={y(0)} y2={y(0)} />
      </svg>

      {hover !== null && (
        <div className="tooltip" style={{ left: Math.min(PADDING.left + slot * hover, Math.max(8, width - 170)), top: 6 }}>
          <div className="when">{rows[hover].label}</div>
          {rows[hover].segments.map((segment) => (
            <div className="row" key={segment.name}>
              <span>
                <span className="legend-swatch" style={{ background: segment.color, display: 'inline-block', marginRight: 6 }} />
                {segment.name}
              </span>
              <b>{segment.value}</b>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
