import { useEffect, useRef, useState } from 'react';

export const SERIES_COLORS = [
  'var(--series-1)',
  'var(--series-2)',
  'var(--series-3)',
  'var(--series-4)',
  'var(--series-5)',
  'var(--series-6)',
  'var(--series-7)',
  'var(--series-8)'
];

export const seriesColor = (index: number) => SERIES_COLORS[index % SERIES_COLORS.length];

export function useElementWidth<T extends HTMLElement>() {
  const ref = useRef<T | null>(null);
  const [width, setWidth] = useState(0);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;

    const observer = new ResizeObserver((entries) => {
      const next = entries[0]?.contentRect.width ?? 0;
      setWidth(Math.round(next));
    });

    observer.observe(element);
    setWidth(Math.round(element.getBoundingClientRect().width));
    return () => observer.disconnect();
  }, []);

  return [ref, width] as const;
}

export interface Scale {
  (value: number): number;
  domain: [number, number];
  range: [number, number];
}

export function linearScale(domain: [number, number], range: [number, number]): Scale {
  const [d0, d1] = domain;
  const [r0, r1] = range;
  const span = d1 - d0 || 1;

  const scale = ((value: number) => r0 + ((value - d0) / span) * (r1 - r0)) as Scale;
  scale.domain = domain;
  scale.range = range;
  return scale;
}

// Rounds a domain out to readable gridline values.
export function niceTicks(min: number, max: number, count = 4): number[] {
  if (!Number.isFinite(min) || !Number.isFinite(max) || min === max) {
    return [min || 0, (min || 0) + 1];
  }

  const step = tickStep((max - min) / count);
  const start = Math.floor(min / step) * step;
  const end = Math.ceil(max / step) * step;
  const ticks: number[] = [];

  for (let value = start; value <= end + step / 2; value += step) {
    ticks.push(Math.round(value * 1e6) / 1e6);
  }

  return ticks;
}

function tickStep(rough: number) {
  const magnitude = 10 ** Math.floor(Math.log10(Math.abs(rough) || 1));
  const normalized = rough / magnitude;
  const snapped = normalized >= 5 ? 10 : normalized >= 2 ? 5 : normalized >= 1 ? 2 : 1;
  return snapped * magnitude;
}

export function linePath(points: { x: number; y: number | null }[]) {
  let path = '';
  let open = false;

  for (const point of points) {
    if (point.y === null) {
      open = false;
      continue;
    }

    path += `${open ? 'L' : 'M'}${point.x.toFixed(1)} ${point.y.toFixed(1)}`;
    open = true;
  }

  return path;
}

export function areaPath(points: { x: number; y: number | null }[], baseline: number) {
  const segments: { x: number; y: number }[][] = [];
  let current: { x: number; y: number }[] = [];

  for (const point of points) {
    if (point.y === null) {
      if (current.length) segments.push(current);
      current = [];
      continue;
    }
    current.push({ x: point.x, y: point.y });
  }

  if (current.length) segments.push(current);

  return segments
    .filter((segment) => segment.length > 1)
    .map((segment) => {
      const head = segment[0];
      const tail = segment[segment.length - 1];
      const line = segment.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join('');
      return `${line}L${tail.x.toFixed(1)} ${baseline}L${head.x.toFixed(1)} ${baseline}Z`;
    })
    .join(' ');
}

export function timeTicks(times: number[], count = 4) {
  if (times.length <= count) return times.map((_, index) => index);

  const step = Math.floor((times.length - 1) / (count - 1));
  const indexes: number[] = [];
  for (let i = 0; i < times.length; i += step) {
    indexes.push(i);
  }

  if (indexes[indexes.length - 1] !== times.length - 1) {
    indexes.push(times.length - 1);
  }

  return indexes;
}

export const axisTime = (value: number) =>
  new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });

export const axisDay = (value: number) =>
  new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });

export function compact(value: number) {
  const absolute = Math.abs(value);
  if (absolute >= 1000) return `${(value / 1000).toFixed(absolute >= 10000 ? 0 : 1)}k`;
  if (absolute >= 10) return value.toFixed(0);
  if (absolute >= 1) return value.toFixed(1);
  return value.toFixed(2);
}
