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

export type MapMatch = { id: string; kind: "Provider" | "Machine"; name: string; key: string };

/** Providers and machines whose name or key contains the query; the map itself is left whole. */
export function findOnMap(map: HostingMap, query: string): MapMatch[] {
  const wanted = query.trim().toLowerCase();
  if (wanted === "") return [];
  const hits = (name: string, key: string) => name.toLowerCase().includes(wanted) || key.toLowerCase().includes(wanted);
  return [
    ...map.providers.filter((provider) => hits(provider.name, provider.key))
      .map((provider): MapMatch => ({ id: `provider:${provider.key}`, kind: "Provider", name: provider.name, key: provider.key })),
    ...map.machines.filter((machine) => hits(machine.name, machine.key))
      .map((machine): MapMatch => ({ id: `machine:${machine.key}`, kind: "Machine", name: machine.name, key: machine.key })),
  ];
}
