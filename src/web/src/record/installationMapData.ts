import type { Edge } from "@xyflow/react";
import type { Schemas } from "@/api/client";
import type { DiagramItem } from "./diagramLayout";

type InstallationMap = Schemas["InstallationMap"];
type Entry = Schemas["InstallationMapEntry"];

/** Display each distinct host named by a recorded URL, in record order. */
export function domainsOf(urls: readonly string[]): string[] {
  const seen = new Set<string>();
  for (const address of urls) {
    try { seen.add(new URL(address).hostname.toLowerCase()); }
    catch { /* An invalid historic URL has no domain to infer. */ }
  }
  return [...seen];
}

export function installationDiagram(map: InstallationMap, showPlatform: boolean): { items: DiagramItem[]; edges: Edge[] } {
  const visible = map.installations.filter((entry) => showPlatform || entry.role === "application");
  const items: DiagramItem[] = [
    { id: `machine:${map.machine}`, type: "machine", data: { map }, width: 224, height: 104,
      ariaLabel: `Machine ${map.name}` },
    ...visible.map((entry: Entry) => {
      const domains = domainsOf(entry.urls);
      return {
        id: `installation:${entry.key}`, type: "installation", data: { entry, domains },
        width: 284, height: 154 + domains.reduce((lines, domain) => lines + Math.max(1, Math.ceil(domain.length / 30)), 0) * 22,
        ariaLabel: `Installation ${entry.name}`,
      };
    }),
  ];
  const edges: Edge[] = visible.map((entry) => ({
    id: `installed:${map.machine}:${entry.key}`,
    source: `machine:${map.machine}`, target: `installation:${entry.key}`,
    type: "smoothstep", selectable: false,
  }));
  return { items, edges };
}
