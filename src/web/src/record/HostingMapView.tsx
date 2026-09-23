import { lazy, Suspense, Component, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { PanelRightCloseIcon, PanelRightOpenIcon } from "lucide-react";
import { api, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { PageHeader } from "@/shared/PageHeader";
import { useAsk } from "@/shared/ask";
import { Failed, Waiting } from "@/shared/Detail";
import { installationsOnMachinePath, machinePath, providerPath } from "./addresses";
import { StatusBadge } from "./Parts";
import { mapDiagram } from "./hostingMapData";

type HostingMap = Schemas["HostingMap"];
type Machine = Schemas["HostingMapMachine"];

const HostingDiagram = lazy(() => import("./HostingDiagram").then((module) => ({ default: module.HostingDiagram })));

/** The graph is supplementary; a rendering failure leaves the grouped list usable. */
class DiagramBoundary extends Component<{ children: ReactNode; onFail: () => void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch() { this.props.onFail(); }
  render() {
    return this.state.failed
      ? <p role="status" className="rounded-lg border p-4 text-sm text-muted-foreground">The diagram is unavailable. The grouped list has every provider and machine.</p>
      : this.props.children;
  }
}

function MachineList({ machine }: { machine: Machine }) {
  const addresses = [machine.ipv4, machine.ipv6, machine.private_ip].filter((address) => address !== null);
  return <li className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t py-2 text-sm">
    <Link className="min-w-0 font-mono text-xs text-brand hover:underline" to={machinePath(machine.key)}>{machine.key}</Link>
    <span className="min-w-0 flex-1 truncate">{machine.name}</span>
    <StatusBadge status={machine.status} />
    <span className="w-full break-all font-mono text-xs text-muted-foreground">{addresses.join(" · ") || "No addresses recorded"}</span>
    <Link className="text-xs text-brand hover:underline" to={installationsOnMachinePath(machine.key)}>Installations</Link>
  </li>;
}

export function HostingMapView() {
  const { asked } = useAsk<HostingMap>("hosting-map", (signal) => api.GET("/api/hosting-map", { signal }));
  if (asked.at === "asking") return <Waiting title="Loading the hosting map…" />;
  if (asked.at === "failed") return <Failed title="Hosting map" why={asked.why}
    back={<Link className="text-brand hover:underline" to="/machines">All machines</Link>} />;
  return <HostingMapContent map={asked.value} />;
}

function HostingMapContent({ map }: { map: HostingMap }) {
  const [listHidden, setListHidden] = useState(false);
  const [diagramFailed, setDiagramFailed] = useState(false);
  const { items, edges } = mapDiagram(map);
  const under = new Map(map.providers.map((provider) => [provider.key, map.machines.filter((machine) => machine.provider === provider.key)]));
  const unassigned = map.machines.filter((machine) => machine.provider === null);
  // Without the diagram the list is the only way to the records, so it cannot stay hidden.
  const listShown = !listHidden || diagramFailed;

  return <>
    <PageHeader title="Hosting map" meta={`${map.providers.length} providers · ${map.machines.length} machines`}>
      {items.length > 0 && !diagramFailed && <Button variant="outline" size="sm" aria-expanded={listShown} aria-controls="hosting-map-list"
        onClick={() => setListHidden((hidden) => !hidden)}>
        {listShown ? <PanelRightCloseIcon aria-hidden /> : <PanelRightOpenIcon aria-hidden />}
        {listShown ? "Hide list" : "Show list"}
      </Button>}
    </PageHeader>
    {items.length === 0 ? <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <p className="font-medium">No providers or machines yet.</p>
      <Link className="text-sm text-brand hover:underline" to="/providers">Browse providers</Link>
    </div> : <div data-list={listShown ? "shown" : "hidden"}
      className={cn("grid min-w-0 gap-5 p-4 md:p-6", listShown && "xl:grid-cols-[minmax(0,2fr)_minmax(19rem,1fr)]")}>
      <DiagramBoundary onFail={() => setDiagramFailed(true)}>
        <Suspense fallback={<p aria-busy className="rounded-lg border p-4 text-sm text-muted-foreground">Loading the diagram…</p>}>
          <HostingDiagram items={items} edges={edges} />
        </Suspense>
      </DiagramBoundary>
      <section id="hosting-map-list" aria-label="Providers and machines" hidden={!listShown} className="min-w-0 space-y-4">
        <h2 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Providers and machines</h2>
        {map.providers.map((provider) => <div key={provider.key} className="rounded-lg border p-3">
          <h3 className="font-semibold"><Link className="text-brand hover:underline" to={providerPath(provider.key)}>{provider.name}</Link></h3>
          <p className="font-mono text-xs text-muted-foreground">{provider.key}</p>
          {(under.get(provider.key) ?? []).length === 0
            ? <p className="mt-2 text-sm text-muted-foreground">No machines assigned.</p>
            : <ul className="mt-2">{under.get(provider.key)!.map((machine) => <MachineList key={machine.key} machine={machine} />)}</ul>}
        </div>)}
        {unassigned.length > 0 && <div className="rounded-lg border p-3">
          <h3 className="font-semibold">Unassigned machines</h3>
          <ul className="mt-2">{unassigned.map((machine) => <MachineList key={machine.key} machine={machine} />)}</ul>
        </div>}
      </section>
    </div>}
  </>;
}
