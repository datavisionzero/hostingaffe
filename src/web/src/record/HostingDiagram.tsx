import { Handle, Position, type Edge, type NodeProps, type NodeTypes } from "@xyflow/react";
import { Link } from "react-router";
import type { Schemas } from "@/api/client";
import { Badge } from "@/components/ui/badge";
import { installationsOnMachinePath, machinePath, providerPath } from "./addresses";
import { cn } from "@/lib/utils";
import { Diagram, type DiagramFocus } from "./Diagram";
import type { DiagramItem } from "./diagramLayout";
import { StatusBadge } from "./Parts";
import { MachineAvatar } from "./avatars/MachineAvatar";
import { ProviderEmblem } from "./emblems/ProviderEmblem";

type Provider = Schemas["HostingMapProvider"];
type Machine = Schemas["HostingMapMachine"];

/** A card the find control brought into view stands out until something else is found. */
const found = "ring-4 ring-brand/60 ring-offset-2 ring-offset-background";

export function ProviderCard({ data, selected }: NodeProps) {
  const provider = data.provider as Provider;
  return <div className={cn("flex h-full items-center gap-3 rounded-xl border border-brand/40 bg-card px-4 py-3 shadow-sm", selected && found)}>
    <ProviderEmblem provider={provider} size={48} />
    <div className="flex min-w-0 flex-1 flex-col">
      <span className="text-[0.65rem] font-semibold tracking-wide text-muted-foreground uppercase">Provider</span>
      <Link className="nodrag nopan truncate font-semibold text-brand hover:underline focus-visible:outline-2 focus-visible:outline-brand"
        to={providerPath(provider.key)}>{provider.name}</Link>
      <span className="truncate font-mono text-xs text-muted-foreground">{provider.key}</span>
    </div>
    <Handle type="source" position={Position.Right} isConnectable={false} className="!size-2 !border-0 !bg-brand" />
  </div>;
}

export function MachineCard({ data, selected }: NodeProps) {
  const machine = data.machine as Machine;
  const addresses = [
    ["IPv4", machine.ipv4], ["IPv6", machine.ipv6], ["Private", machine.private_ip],
  ].filter(([, value]) => value !== null);
  return <div className={cn("flex h-full flex-col rounded-xl border bg-card px-4 py-3 shadow-sm", selected && found)}>
    <Handle type="target" position={Position.Left} isConnectable={false} className="!size-2 !border-0 !bg-brand" />
    <div className="flex items-start gap-3">
      <MachineAvatar machine={machine} size={48} />
      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex items-center gap-2">
          <span className="mr-auto truncate text-[0.65rem] font-semibold tracking-wide text-muted-foreground uppercase">{machine.kind}</span>
          <StatusBadge status={machine.status} />
        </div>
        <Link className="nodrag nopan truncate font-semibold text-brand hover:underline focus-visible:outline-2 focus-visible:outline-brand"
          to={machinePath(machine.key)}>{machine.name}</Link>
        <span className="truncate font-mono text-xs text-muted-foreground">{machine.key}</span>
      </div>
    </div>
    <div className="mt-2 min-h-0 flex-1 space-y-0.5 overflow-hidden font-mono text-[0.68rem] text-muted-foreground">
      {addresses.length === 0 ? <span>No addresses recorded</span> : addresses.map(([label, value]) =>
        <div key={label} className="truncate"><span className="mr-1 text-foreground">{label}</span>{value}</div>)}
    </div>
    <div className="mt-1 flex items-center justify-between gap-2 text-xs">
      {machine.provider === null && <Badge variant="outline">Unassigned</Badge>}
      <Link className="nodrag nopan ml-auto text-brand hover:underline focus-visible:outline-2 focus-visible:outline-brand"
        to={installationsOnMachinePath(machine.key)}>Installations</Link>
    </div>
  </div>;
}

const nodeTypes: NodeTypes = { provider: ProviderCard, machine: MachineCard };

export function HostingDiagram({ items, edges, focus, frame, className }: {
  items: DiagramItem[];
  edges: Edge[];
  focus: DiagramFocus | null;
  frame: string;
  className?: string;
}) {
  return <Diagram label="Provider to machine diagram" items={items} edges={edges} nodeTypes={nodeTypes}
    focus={focus} frame={frame} className={className} />;
}
