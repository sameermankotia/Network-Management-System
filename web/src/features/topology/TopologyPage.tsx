import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import { mbps, ms, pct } from '@/lib/format';
import { Empty, Segmented, Skeleton, StatusPill } from '@/components/Bits';
import { useElementWidth } from '@/charts/primitives';
import { computeLayout, edgeWidth, nodeGlyph, nodeRadius, statusColor, type Placed } from './layout';
import type { TopologyEdge, TopologyNode } from '@/lib/types';

const HEIGHT = 560;

export function TopologyPage() {
  const [site, setSite] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [tracing, setTracing] = useState(false);

  const { data: graph, isLoading } = useQuery({
    queryKey: ['topology', site],
    queryFn: () => api.topology(site || undefined),
    refetchInterval: 30_000
  });

  const { data: path, refetch: tracePath } = useQuery({
    queryKey: ['path', from, to],
    queryFn: () => api.path(from, to),
    enabled: false
  });

  const [wrapRef, width] = useElementWidth<HTMLDivElement>();
  const [positions, setPositions] = useState<Placed[]>([]);
  const [drag, setDrag] = useState<{ id: string; dx: number; dy: number } | null>(null);
  const [view, setView] = useState({ x: 0, y: 0, scale: 1 });
  const panRef = useRef<{ x: number; y: number } | null>(null);

  useEffect(() => {
    if (!graph || width <= 0) return;
    setPositions(computeLayout(graph.nodes, graph.edges, width, HEIGHT));
  }, [graph, width]);

  const byId = useMemo(() => new Map(positions.map((node) => [node.id, node])), [positions]);
  const nodesById = useMemo(() => new Map((graph?.nodes ?? []).map((node) => [node.id, node])), [graph]);
  const highlighted = useMemo(() => new Set((path?.hops ?? []).map((hop) => hop.deviceId)), [path]);

  const onPointerMove = (event: React.PointerEvent<SVGSVGElement>) => {
    if (drag) {
      const point = toSvg(event, view);
      setPositions((current) =>
        current.map((node) => (node.id === drag.id ? { ...node, x: point.x - drag.dx, y: point.y - drag.dy } : node))
      );
      return;
    }

    if (panRef.current) {
      const dx = event.clientX - panRef.current.x;
      const dy = event.clientY - panRef.current.y;
      panRef.current = { x: event.clientX, y: event.clientY };
      setView((current) => ({ ...current, x: current.x + dx, y: current.y + dy }));
    }
  };

  const onWheel = (event: React.WheelEvent<SVGSVGElement>) => {
    event.preventDefault();
    setView((current) => ({
      ...current,
      scale: Math.min(2.2, Math.max(0.5, current.scale * (event.deltaY > 0 ? 0.92 : 1.08)))
    }));
  };

  const selectedNode = selected ? nodesById.get(selected) : undefined;

  return (
    <div className="page">
      <div className="toolbar">
        <Segmented
          options={[{ label: 'All sites', value: '' }, ...(graph?.sites ?? []).map((value) => ({ label: value, value }))]}
          value={site}
          onChange={setSite}
        />
        <span className="spacer" />

        <select value={from} onChange={(event) => setFrom(event.target.value)}>
          <option value="">Trace from…</option>
          {(graph?.nodes ?? []).map((node) => (
            <option key={node.id} value={node.id}>
              {node.name}
            </option>
          ))}
        </select>
        <select value={to} onChange={(event) => setTo(event.target.value)}>
          <option value="">…to</option>
          {(graph?.nodes ?? []).map((node) => (
            <option key={node.id} value={node.id}>
              {node.name}
            </option>
          ))}
        </select>
        <button
          className="btn"
          disabled={!from || !to || from === to}
          onClick={async () => {
            setTracing(true);
            await tracePath();
            setTracing(false);
          }}
        >
          {tracing ? 'Tracing…' : 'Trace path'}
        </button>
        <button className="btn ghost" onClick={() => setView({ x: 0, y: 0, scale: 1 })}>
          Reset view
        </button>
      </div>

      {path && (
        <section className="card">
          <div className="card-head">
            <h2>Connectivity path</h2>
            <span className="hint">{path.found ? `${path.hops.length} hops` : 'no path'}</span>
          </div>
          {!path.found ? (
            <Empty title="No documented path" hint={path.message ?? undefined} />
          ) : (
            <>
              {path.message && <div className="chip" style={{ marginBottom: 10, color: 'var(--warning)' }}>{path.message}</div>}
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
                {path.hops.map((hop, index) => (
                  <span key={hop.deviceId} style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                    {index > 0 && <span className="muted">→</span>}
                    <span className="chip">
                      <StatusPill status={hop.status} label={hop.name} />
                      {hop.utilizationPct !== null && hop.utilizationPct !== undefined && (
                        <span className="muted">{pct(hop.utilizationPct)}</span>
                      )}
                    </span>
                  </span>
                ))}
              </div>
            </>
          )}
        </section>
      )}

      <div className="topology" ref={wrapRef}>
        {isLoading || !graph ? (
          <div style={{ padding: 16 }}>
            <Skeleton height={HEIGHT - 32} />
          </div>
        ) : (
          <svg
            height={HEIGHT}
            className={drag || panRef.current ? 'dragging' : ''}
            onPointerMove={onPointerMove}
            onPointerUp={() => {
              setDrag(null);
              panRef.current = null;
            }}
            onPointerLeave={() => {
              setDrag(null);
              panRef.current = null;
            }}
            onPointerDown={(event) => {
              panRef.current = { x: event.clientX, y: event.clientY };
            }}
            onWheel={onWheel}
          >
            <g transform={`translate(${view.x} ${view.y}) scale(${view.scale})`}>
              {graph.edges.map((edge) => (
                <Edge
                  key={edge.id}
                  edge={edge}
                  a={byId.get(edge.source)}
                  b={byId.get(edge.target)}
                  highlight={highlighted.has(edge.source) && highlighted.has(edge.target)}
                />
              ))}

              {graph.nodes.map((node) => {
                const placed = byId.get(node.id);
                if (!placed) return null;
                return (
                  <Node
                    key={node.id}
                    node={node}
                    placed={placed}
                    active={selected === node.id}
                    highlighted={highlighted.has(node.id)}
                    onSelect={() => setSelected(node.id === selected ? null : node.id)}
                    onDragStart={(event) => {
                      event.stopPropagation();
                      const point = toSvg(event, view);
                      setDrag({ id: node.id, dx: point.x - placed.x, dy: point.y - placed.y });
                    }}
                  />
                );
              })}
            </g>
          </svg>
        )}

        <div className="topology-overlay">
          <div className="topology-legend">
            <strong style={{ fontSize: 11, letterSpacing: '0.06em', textTransform: 'uppercase', color: 'var(--ink-muted)' }}>
              State
            </strong>
            {['Up', 'Degraded', 'Down', 'Unknown'].map((status) => (
              <span key={status} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <span style={{ width: 10, height: 3, borderRadius: 2, background: statusColor(status) }} />
                {status}
              </span>
            ))}
            <span className="muted" style={{ marginTop: 4 }}>Thicker links carry more capacity</span>
          </div>
        </div>

        {selectedNode && (
          <div
            className="topology-legend"
            style={{ position: 'absolute', right: 12, top: 12, width: 250, gap: 8 }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <strong style={{ fontSize: 13 }}>{selectedNode.name}</strong>
              <StatusPill status={selectedNode.status} />
            </div>
            <span className="mono">{selectedNode.hostname}</span>
            <span>
              {selectedNode.site}
              {selectedNode.building ? ` · ${selectedNode.building}` : ''}
            </span>
            <span>Latency {ms(selectedNode.latencyMs)} · CPU {pct(selectedNode.cpuPct)}</span>
            {selectedNode.openAlerts > 0 && (
              <span style={{ color: 'var(--critical)' }}>{selectedNode.openAlerts} open alert(s)</span>
            )}
            <Link className="btn sm" to={`/devices/${selectedNode.id}`}>
              Open device
            </Link>
          </div>
        )}
      </div>
    </div>
  );
}

function Edge({
  edge,
  a,
  b,
  highlight
}: {
  edge: TopologyEdge;
  a?: Placed;
  b?: Placed;
  highlight: boolean;
}) {
  const [hover, setHover] = useState(false);
  if (!a || !b) return null;

  const utilization = edge.utilizationPct ?? 0;
  const color = statusColor(edge.status);

  return (
    <g onPointerEnter={() => setHover(true)} onPointerLeave={() => setHover(false)}>
      <line
        x1={a.x}
        y1={a.y}
        x2={b.x}
        y2={b.y}
        stroke="transparent"
        strokeWidth={14}
        style={{ cursor: 'help' }}
      />
      <line
        className="edge"
        x1={a.x}
        y1={a.y}
        x2={b.x}
        y2={b.y}
        stroke={highlight ? 'var(--series-1)' : color}
        strokeWidth={highlight ? edgeWidth(edge.capacityMbps) + 1.4 : edgeWidth(edge.capacityMbps)}
        strokeOpacity={edge.status === 'Down' ? 0.9 : 0.35 + Math.min(0.55, utilization / 120)}
        strokeDasharray={edge.linkType === 'Wireless' ? '5 4' : undefined}
      />
      {(hover || highlight) && (
        <text
          className="node-label"
          x={(a.x + b.x) / 2}
          y={(a.y + b.y) / 2 - 7}
          fill="var(--ink)"
          style={{ fontSize: 10 }}
        >
          {edge.linkType} · {mbps(edge.trafficMbps)} of {edge.capacityMbps >= 1000 ? `${edge.capacityMbps / 1000}G` : `${edge.capacityMbps}M`}
          {edge.utilizationPct !== null && edge.utilizationPct !== undefined ? ` · ${pct(edge.utilizationPct)}` : ''}
        </text>
      )}
    </g>
  );
}

function Node({
  node,
  placed,
  active,
  highlighted,
  onSelect,
  onDragStart
}: {
  node: TopologyNode;
  placed: Placed;
  active: boolean;
  highlighted: boolean;
  onSelect: () => void;
  onDragStart: (event: React.PointerEvent<SVGGElement>) => void;
}) {
  const radius = nodeRadius(node.tier);
  const color = statusColor(node.status);

  return (
    <g
      transform={`translate(${placed.x} ${placed.y})`}
      onPointerDown={onDragStart}
      onClick={onSelect}
      style={{ cursor: 'pointer' }}
    >
      {(active || highlighted) && <circle r={radius + 7} fill="var(--accent-soft)" />}
      {node.openAlerts > 0 && <circle r={radius + 4} fill="none" stroke="var(--critical)" strokeWidth={1.5} strokeOpacity={0.6} />}
      <circle r={radius} fill="var(--surface-raised)" stroke={color} strokeWidth={2.5} />
      <text textAnchor="middle" y={4} fontSize={radius > 16 ? 13 : 11} fontWeight={600} fill="var(--ink)">
        {nodeGlyph(node.deviceType)}
      </text>
      <text className="node-label" y={radius + 14}>
        {node.name}
      </text>
      {node.openAlerts > 0 && (
        <text y={-radius - 7} textAnchor="middle" fontSize={10} fill="var(--critical)" fontWeight={600}>
          {node.openAlerts}
        </text>
      )}
    </g>
  );
}

function toSvg(event: React.PointerEvent, view: { x: number; y: number; scale: number }) {
  const target = event.currentTarget as Element;
  const svg = target.closest('svg');
  const box = (svg ?? target).getBoundingClientRect();
  return {
    x: (event.clientX - box.left - view.x) / view.scale,
    y: (event.clientY - box.top - view.y) / view.scale
  };
}
