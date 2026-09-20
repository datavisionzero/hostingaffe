import dagre from "@dagrejs/dagre";
import { Position, type Edge, type Node } from "@xyflow/react";

export type DiagramItem = {
  id: string;
  type: string;
  data: Record<string, unknown>;
  width: number;
  height: number;
  ariaLabel: string;
};

/** Place fixed-size read-only cards by their recorded relationships. */
export function layoutDiagram(items: DiagramItem[], edges: Edge[]): Node[] {
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir: "LR", nodesep: 36, ranksep: 96, marginx: 28, marginy: 28 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const item of items) graph.setNode(item.id, { width: item.width, height: item.height });
  for (const edge of edges) graph.setEdge(edge.source, edge.target);
  dagre.layout(graph);

  return items.map((item) => {
    const at = graph.node(item.id);
    return {
      id: item.id,
      type: item.type,
      data: item.data,
      ariaLabel: item.ariaLabel,
      position: { x: at.x - item.width / 2, y: at.y - item.height / 2 },
      sourcePosition: Position.Right,
      targetPosition: Position.Left,
      style: { width: item.width, height: item.height },
      draggable: false,
      deletable: false,
    };
  });
}
