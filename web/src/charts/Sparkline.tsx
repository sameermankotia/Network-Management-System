import { linePath, linearScale } from './primitives';

interface Props {
  values: (number | null)[];
  width?: number;
  height?: number;
  color?: string;
  fill?: boolean;
}

export function Sparkline({ values, width = 84, height = 24, color = 'var(--series-1)', fill = true }: Props) {
  const numbers = values.filter((value): value is number => value !== null);
  if (numbers.length < 2) {
    return <span className="muted">—</span>;
  }

  const min = Math.min(...numbers);
  const max = Math.max(...numbers);
  const y = linearScale([min === max ? min - 1 : min, max], [height - 2, 2]);
  const x = linearScale([0, values.length - 1], [1, width - 1]);
  const points = values.map((value, index) => ({ x: x(index), y: value === null ? null : y(value) }));
  const path = linePath(points);
  const last = points[points.length - 1];

  return (
    <svg width={width} height={height} aria-hidden="true" style={{ display: 'block' }}>
      {fill && <path d={`${path}L${width - 1} ${height} L1 ${height} Z`} fill={color} opacity={0.12} />}
      <path d={path} fill="none" stroke={color} strokeWidth={1.5} strokeLinecap="round" />
      {last.y !== null && <circle cx={last.x} cy={last.y} r={2} fill={color} />}
    </svg>
  );
}
