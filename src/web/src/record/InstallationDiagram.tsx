import { Handle, Position, type Edge, type NodeProps, type NodeTypes } from "@xyflow/react";
import { Link } from "react-router";
import type { Schemas } from "@/api/client";
import { moment } from "@/shared/when";
import { installationPath, machinePath } from "./addresses";
import { Diagram } from "./Diagram";
import type { DiagramItem } from "./diagramLayout";
import { StatusBadge } from "./Parts";
import { MachineAvatar } from "./avatars/MachineAvatar";

type InstallationMap = Schemas["InstallationMap"];
type Entry = Schemas["InstallationMapEntry"];

function MachineCard({ data }: NodeProps) {
  const map = data.map as InstallationMap;
  return <div className="flex h-full items-center gap-3 rounded-xl border border-brand/40 bg-card px-4 py-3 shadow-sm">
    <MachineAvatar machine={{ key: map.machine, avatar: map.avatar, avatar_color: map.avatar_color }} size={40} />
    <div className="flex min-w-0 flex-col">
      <span className="text-[0.65rem] font-semibold tracking-wide text-muted-foreground uppercase">Machine</span>
      <Link className="nodrag nopan truncate font-semibold text-brand hover:underline focus-visible:outline-2 focus-visible:outline-brand"
        to={machinePath(map.machine)}>{map.name}</Link>
      <span className="truncate font-mono text-xs text-muted-foreground">{map.machine}</span>
    </div>
    <Handle type="source" position={Position.Right} isConnectable={false} className="!size-2 !border-0 !bg-brand" />
  </div>;
}

function InstallationCard({ data }: NodeProps) {
  const entry = data.entry as Entry;
  const domains = data.domains as string[];
  return <div className="flex h-full flex-col rounded-xl border bg-card px-4 py-3 shadow-sm">
    <Handle type="target" position={Position.Left} isConnectable={false} className="!size-2 !border-0 !bg-brand" />
    <div className="flex items-center justify-between gap-2">
      <span className="text-[0.65rem] font-semibold tracking-wide text-muted-foreground uppercase">{entry.role}</span>
      <StatusBadge status={entry.status} />
    </div>
    <Link className="nodrag nopan mt-1 truncate font-semibold text-brand hover:underline focus-visible:outline-2 focus-visible:outline-brand"
      to={installationPath(entry.key)}>{entry.name}</Link>
    <span className="truncate font-mono text-xs text-muted-foreground">{entry.key} · {entry.software}</span>
    <div className="mt-2 min-h-0 flex-1 font-mono text-xs">
      {domains.map((domain) => <div key={domain} className="break-all">{domain}</div>)}
    </div>
    <span className="mt-1 text-[0.68rem] text-muted-foreground">
      {entry.latest_deployment_at === null ? "No deployment recorded" : `Latest deployment ${moment(entry.latest_deployment_at)}`}
    </span>
  </div>;
}

const nodeTypes: NodeTypes = { machine: MachineCard, installation: InstallationCard };

export function InstallationDiagram({ items, edges }: { items: DiagramItem[]; edges: Edge[] }) {
  return <Diagram label="Machine to installation diagram" items={items} edges={edges} nodeTypes={nodeTypes} />;
}
