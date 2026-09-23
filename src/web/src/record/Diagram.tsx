import { useEffect, useRef } from "react";
import { Background, Controls, ReactFlow, useReactFlow, useStore, type Edge, type NodeTypes } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { cn } from "@/lib/utils";
import { layoutDiagram, type DiagramItem } from "./diagramLayout";

/** A node to bring into view; `at` changes each time it is asked for, so asking twice moves the view twice. */
export type DiagramFocus = { id: string; at: number };

/**
 * Moves only the view: onto the focused node when one is asked for, and again
 * once the canvas has taken a new size after its surroundings changed (`frame`).
 * The cards carry their fixed size, so a node's bounds are known without
 * waiting for React Flow to measure it.
 */
function Framing({ focus, frame }: { focus: DiagramFocus | null; frame: string | undefined }) {
  const { fitView } = useReactFlow();
  const size = useStore((state) => state.width > 0 && state.height > 0 ? `${state.width}x${state.height}` : null);
  const framed = useRef<{ focus: DiagramFocus | null; frame: string | undefined; size: string } | null>(null);
  useEffect(() => {
    if (size === null) return;
    const last = framed.current;
    const reframed = last !== null && last.frame !== frame;
    // A new frame is only worth fitting into once the canvas has actually been resized.
    if (reframed && last.size === size && last.focus === focus) return;
    framed.current = { focus, frame, size };
    // The first fit of the whole map is React Flow's own.
    if (last === null ? focus === null : last.focus === focus && !reframed) return;
    void fitView(focus === null
      ? { padding: 0.15, maxZoom: 1, duration: 200 }
      : { nodes: [{ id: focus.id }], padding: 0.4, maxZoom: 1, duration: 300 });
  }, [focus, frame, size, fitView]);
  return null;
}

export function Diagram({ label, items, edges, nodeTypes, focus = null, frame, className }: {
  label: string;
  items: DiagramItem[];
  edges: Edge[];
  nodeTypes: NodeTypes;
  focus?: DiagramFocus | null;
  frame?: string;
  className?: string;
}) {
  const nodes = layoutDiagram(items, edges).map((node) => node.id === focus?.id ? { ...node, selected: true } : node);
  return <div className={cn("h-[34rem] min-h-80 overflow-hidden rounded-lg border bg-background", className)} role="region" aria-label={label}>
    <ReactFlow
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      fitView
      fitViewOptions={{ padding: 0.15, maxZoom: 1 }}
      minZoom={0.2}
      maxZoom={2}
      nodesDraggable={false}
      nodesConnectable={false}
      edgesFocusable={false}
      edgesReconnectable={false}
      deleteKeyCode={null}
      zoomOnScroll={false}
      panOnDrag
      attributionPosition="bottom-left"
    >
      <Background gap={24} size={1} />
      <Controls showInteractive={false} />
      <Framing focus={focus} frame={frame} />
    </ReactFlow>
  </div>;
}
