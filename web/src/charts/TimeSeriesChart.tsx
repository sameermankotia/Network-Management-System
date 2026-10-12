import { useMemo, useState } from 'react';
import type { NamedSeries } from '@/lib/types';
import { areaPath, axisDay, axisTime, compact, linePath, linearScale, niceTicks, seriesColor, timeTicks, useElementWidth } from './primitives';

interface Props {
  series: NamedSeries[];
  unit?: string;
  height?: number;
  area?: boolean;
  maxHint?: number;
  byDay?: boolean;
  format?: (value: number) => string;
}

const PADDING = { top: 10, right: 14, bottom: 22, left: 44 };

export function TimeSeriesChart({ series, unit = '', height = 190, area = false, maxHint, byDay = false, format }: Props) {
  const [wrapRef, width] = useElementWidth<HTMLDivElement>();
  const [hidden, setHidden] = useState<Record<string, boolean>>({});
  const [hover, setHover] = useState<number | null>(null);

  const visible = series.filter((s) => !hidden[s.name]);
  const times = useMemo(
    () => (series[0]?.points ?? []).map((point) => new Date(point.at).getTime()),
    [series]
  );

  const plotWidth = Math.max(0, width - PADDING.left - PADDING.right);
  const plotHeight = height - PADDING.top - PADDING.bottom;

  const values = visible.flatMap((s) => s.points.map((p) => p.value)).filter((v): v is number => v !== null);
  const peak = Math.max(maxHint ?? 0, ...(values.length ? values : [1]));
  const ticks = niceTicks(0, peak, 4);
  const y = linearScale([0, ticks[ticks.length - 1]], [PADDING.top + plotHeight, PADDING.top]);
  const x = linearScale([0, Math.max(1, times.length - 1)], [PADDING.left, PADDING.left + plotWidth]);
  const label = format ?? ((value: number) => `${compact(value)}${unit ? ` ${unit}` : ''}`);

  const nearest = (event: React.MouseEvent<SVGSVGElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    const offset = event.clientX - box.left - PADDING.left;
    const ratio = plotWidth === 0 ? 0 : offset / plotWidth;
    const index = Math.round(ratio * Math.max(1, times.length - 1));
    setHover(Math.min(times.length - 1, Math.max(0, index)));
  };

  if (!times.length) {
    return (
      <div className="chart" ref={wrapRef}>
        <div className="empty">No samples in this window yet.</div>
      </div>
    );
  }

  const tooltipLeft = hover === null ? 0 : Math.min(Math.max(x(hover) + 12, 8), Math.max(8, width - 170));

  return (
    <div className="chart" ref={wrapRef}>
      {series.length > 1 && (
        <div className="legend">
          {series.map((s, index) => (
            <span
              key={s.name}
              className={`legend-item ${hidden[s.name] ? 'off' : ''}`}
              onClick={() => setHidden((current) => ({ ...current, [s.name]: !current[s.name] }))}
            >
              <span className="legend-swatch" style={{ background: seriesColor(index) }} />
              {s.name}
            </span>
          ))}
        </div>
      )}

      <svg
        height={height}
        role="img"
        aria-label={`${series.map((s) => s.name).join(', ')} over time`}
        onMouseMove={nearest}
        onMouseLeave={() => setHover(null)}
      >
        <g className="axis">
          {ticks.map((tick) => (
            <g key={tick}>
              <line className="gridline" x1={PADDING.left} x2={PADDING.left + plotWidth} y1={y(tick)} y2={y(tick)} />
              <text x={PADDING.left - 8} y={y(tick) + 3} textAnchor="end">
                {compact(tick)}
              </text>
            </g>
          ))}
          {timeTicks(times).map((index) => (
            <text key={index} x={x(index)} y={height - 6} textAnchor="middle">
              {byDay ? axisDay(times[index]) : axisTime(times[index])}
            </text>
          ))}
        </g>

        {visible.map((s) => {
          const index = series.findIndex((candidate) => candidate.name === s.name);
          const points = s.points.map((point, i) => ({
            x: x(i),
            y: point.value === null ? null : y(point.value)
          }));

          return (
            <g key={s.name}>
              {area && (
                <path d={areaPath(points, PADDING.top + plotHeight)} fill={seriesColor(index)} opacity={0.14} />
              )}
              <path
                d={linePath(points)}
                fill="none"
                stroke={seriesColor(index)}
                strokeWidth={2}
                strokeLinejoin="round"
                strokeLinecap="round"
              />
            </g>
          );
        })}

        {hover !== null && (
          <g>
            <line
              className="baseline"
              x1={x(hover)}
              x2={x(hover)}
              y1={PADDING.top}
              y2={PADDING.top + plotHeight}
              strokeDasharray="3 3"
            />
            {visible.map((s) => {
              const index = series.findIndex((candidate) => candidate.name === s.name);
              const point = s.points[hover];
              if (!point || point.value === null) return null;
              return (
                <circle
                  key={s.name}
                  cx={x(hover)}
                  cy={y(point.value)}
                  r={4}
                  fill={seriesColor(index)}
                  stroke="var(--surface)"
                  strokeWidth={2}
                />
              );
            })}
          </g>
        )}

        <line className="baseline" x1={PADDING.left} x2={PADDING.left + plotWidth} y1={y(0)} y2={y(0)} />
      </svg>

      {hover !== null && (
        <div className="tooltip" style={{ left: tooltipLeft, top: 6 }}>
          <div className="when">{new Date(times[hover]).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</div>
          {visible.map((s) => {
            const index = series.findIndex((candidate) => candidate.name === s.name);
            const point = s.points[hover];
            return (
              <div className="row" key={s.name}>
                <span>
                  <span className="legend-swatch" style={{ background: seriesColor(index), display: 'inline-block', marginRight: 6 }} />
                  {s.name}
                </span>
                <b>{point?.value === null || point?.value === undefined ? '—' : label(point.value)}</b>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
