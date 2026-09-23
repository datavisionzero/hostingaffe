import { useEffect } from "react";
import { Background, Controls, ReactFlow, useReactFlow, type Edge, type NodeTypes } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { layoutDiagram, type DiagramItem } from "./diagramLayout";

/** A node to bring into view; `at` changes each time it is asked for, so asking twice moves the view twice. */
export type DiagramFocus = { id: string; at: number };

/**
 * Moves only the view onto the focused node. The cards carry their fixed size,
 * so the node's bounds are known without waiting for React Flow to measure it.
 */
function FocusOn({ focus }: { focus: DiagramFocus | null }) {
  const { fitView } = useReactFlow();
  useEffect(() => {
    if (focus !== null) void fitView({ nodes: [{ id: focus.id }], padding: 0.4, maxZoom: 1, duration: 300 });
  }, [focus, fitView]);
  return null;
}

export function Diagram({ label, items, edges, nodeTypes, focus = null }: {
  label: string;
  items: DiagramItem[];
  edges: Edge[];
  nodeTypes: NodeTypes;
  focus?: DiagramFocus | null;
}) {
  const nodes = layoutDiagram(items, edges).map((node) => node.id === focus?.id ? { ...node, selected: true } : node);
  return <div className="h-[34rem] min-h-80 overflow-hidden rounded-lg border bg-background" role="region" aria-label={label}>
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
      <FocusOn focus={focus} />
    </ReactFlow>
  </div>;
}
