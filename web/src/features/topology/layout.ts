import { forceCollide, forceLink, forceManyBody, forceSimulation, forceX, forceY, type Simulation } from 'd3-force';
import type { TopologyEdge, TopologyNode } from '@/lib/types';

export interface Placed {
  id: string;
  x: number;
  y: number;
}

interface SimNode {
  id: string;
  tier: number;
  x?: number;
  y?: number;
  fx?: number | null;
  fy?: number | null;
}

interface SimEdge {
  source: string;
  target: string;
  capacityMbps: number;
}

// Runs the simulation to completion once instead of animating it: the graph is
// small, and a stable layout is far easier to read than a drifting one. Tier
// (hop distance from the core) pins the vertical band, so the picture matches
// how the network is actually built.
export function computeLayout(
  nodes: TopologyNode[],
  edges: TopologyEdge[],
  width: number,
  height: number
): Placed[] {
  if (!nodes.length || width <= 0) {
    return [];
  }

  const tiers = Math.max(...nodes.map((node) => node.tier)) + 1;
  const band = height / (tiers + 0.6);

  const simNodes: SimNode[] = nodes.map((node, index) => ({
    id: node.id,
    tier: node.tier,
    x: width / 2 + Math.cos(index) * width * 0.2,
    y: band * (node.tier + 0.8)
  }));

  const simEdges: SimEdge[] = edges.map((edge) => ({
    source: edge.source,
    target: edge.target,
    capacityMbps: edge.capacityMbps
  }));

  const simulation: Simulation<SimNode, SimEdge> = forceSimulation(simNodes)
    .force(
      'link',
      forceLink<SimNode, SimEdge>(simEdges)
        .id((node) => node.id)
        .distance((edge) => (edge.capacityMbps >= 10000 ? 140 : 95))
        .strength(0.7)
    )
    .force('charge', forceManyBody().strength(-520))
    .force('collide', forceCollide(42))
    .force('x', forceX(width / 2).strength(0.06))
    .force('y', forceY<SimNode>((node) => band * (node.tier + 0.8)).strength(1.1))
    .stop();

  simulation.tick(420);

  const margin = 54;
  return simNodes.map((node) => ({
    id: node.id,
    x: clamp(node.x ?? width / 2, margin, width - margin),
    y: clamp(node.y ?? height / 2, margin, height - margin)
  }));
}

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

export function edgeWidth(capacityMbps: number) {
  if (capacityMbps >= 10000) return 3.2;
  if (capacityMbps >= 1000) return 2;
  return 1.4;
}

export function statusColor(status: string) {
  switch (status) {
    case 'Up':
      return 'var(--good)';
    case 'Degraded':
      return 'var(--warning)';
    case 'Down':
      return 'var(--critical)';
    case 'Maintenance':
      return 'var(--series-7)';
    default:
      return 'var(--ink-muted)';
  }
}

export function nodeGlyph(type: string) {
  switch (type) {
    case 'Router':
      return 'R';
    case 'Switch':
      return 'S';
    case 'Firewall':
      return 'F';
    case 'AccessPoint':
      return 'W';
    case 'Server':
      return 'D';
    case 'Ups':
      return 'U';
    default:
      return 'P';
  }
}

export function nodeRadius(tier: number) {
  return tier === 0 ? 20 : tier === 1 ? 17 : 14;
}
