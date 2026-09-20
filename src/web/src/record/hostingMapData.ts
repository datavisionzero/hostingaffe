import type { Edge } from "@xyflow/react";
import type { Schemas } from "@/api/client";
import type { DiagramItem } from "./diagramLayout";

type HostingMap = Schemas["HostingMap"];

export function mapDiagram(map: HostingMap): { items: DiagramItem[]; edges: Edge[] } {
  const providers = new Set(map.providers.map((provider) => provider.key));
  const items: DiagramItem[] = [
    ...map.providers.map((provider) => ({
      id: `provider:${provider.key}`, type: "provider", data: { provider },
      width: 232, height: 92, ariaLabel: `Provider ${provider.name}`,
    })),
    ...map.machines.map((machine) => ({
      id: `machine:${machine.key}`, type: "machine", data: { machine },
      width: 260, height: 184, ariaLabel: `Machine ${machine.name}`,
    })),
  ];
  const edges: Edge[] = map.machines
    .filter((machine) => machine.provider !== null && providers.has(machine.provider))
    .map((machine) => ({
      id: `hosted:${machine.provider}:${machine.key}`,
      source: `provider:${machine.provider}`,
      target: `machine:${machine.key}`,
      type: "smoothstep",
      selectable: false,
    }));
  return { items, edges };
}
