import { Background, Controls, ReactFlow, type Edge, type NodeTypes } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { layoutDiagram, type DiagramItem } from "./diagramLayout";

export function Diagram({ label, items, edges, nodeTypes }: {
  label: string;
  items: DiagramItem[];
  edges: Edge[];
  nodeTypes: NodeTypes;
}) {
  const nodes = layoutDiagram(items, edges);
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
    </ReactFlow>
  </div>;
}
